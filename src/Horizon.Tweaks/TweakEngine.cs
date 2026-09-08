using System.Text.Json;
using Horizon.Core.Interfaces;
using Horizon.Core.Models;

namespace Horizon.Tweaks;

public sealed class TweakEngine : ITweakEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ITweakCatalog _catalog;
    private readonly ICompatibilityScanner _compatibility;
    private readonly ITweakOperationRegistry _operations;
    private readonly IActiveChangeStore _activeChanges;
    private readonly IHistoryService _history;
    private readonly ISystemInfoService _systemInfo;
    private readonly IEntitlementService _entitlements;
    private readonly ISettingsService _settings;
    private readonly ICommandRunner _runner;
    private readonly SemaphoreSlim _executionGate = new(1, 1);
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private IReadOnlyList<TweakListItem>? _scanCache;
    private DateTimeOffset _scanCachedAt;
    private static readonly TimeSpan ScanCacheLifetime = TimeSpan.FromMinutes(1);

    public TweakEngine(ITweakCatalog catalog, ICompatibilityScanner compatibility, ITweakOperationRegistry operations,
        IActiveChangeStore activeChanges, IHistoryService history, ISystemInfoService systemInfo,
        IEntitlementService entitlements, ISettingsService settings, ICommandRunner runner)
    {
        _catalog = catalog; _compatibility = compatibility; _operations = operations; _activeChanges = activeChanges;
        _history = history; _systemInfo = systemInfo; _entitlements = entitlements; _settings = settings; _runner = runner;
    }

    public Task<IReadOnlyList<TweakListItem>> ScanAsync(CancellationToken cancellationToken = default) => ScanAsync(false, cancellationToken);
    public Task<IReadOnlyList<TweakListItem>> RefreshScanAsync(CancellationToken cancellationToken = default) => ScanAsync(true, cancellationToken);

    private async Task<IReadOnlyList<TweakListItem>> ScanAsync(bool force, CancellationToken cancellationToken)
    {
        if (!force && TryGetFreshScan(out var cached)) return cached;
        await _scanGate.WaitAsync(cancellationToken);
        try
        {
            if (!force && TryGetFreshScan(out cached)) return cached;
            var definitions = _catalog.GetAll();
            var statuses = await _compatibility.ScanAsync(definitions, cancellationToken);
            _scanCache = definitions.Select(definition => new TweakListItem(definition, statuses[definition.Id])).ToList();
            _scanCachedAt = DateTimeOffset.UtcNow;
            return _scanCache;
        }
        finally { _scanGate.Release(); }
    }

    public Task<OptimizationSession> ApplyAsync(IEnumerable<string> tweakIds, CancellationToken cancellationToken = default) =>
        ApplySessionAsync(tweakIds, "Horizon Optimization", "user", cancellationToken);

    public async Task<OptimizationSession> ApplySessionAsync(IEnumerable<string> tweakIds, string name, string source, CancellationToken cancellationToken = default)
    {
        await _executionGate.WaitAsync(cancellationToken);
        try
        {
            var sessionId = Guid.NewGuid();
            var started = DateTimeOffset.UtcNow;
            var definitions = Resolve(tweakIds);
            var system = await _systemInfo.ScanAsync(cancellationToken);
            var settings = await _settings.LoadAsync(cancellationToken);
            var restorePointCreated = false;
            if (settings.CreateRestorePoint && definitions.Any(definition => definition.RequiresAdmin && definition.Actionable))
                restorePointCreated = await TryCreateRestorePointAsync(name, system, cancellationToken);
            var changes = new List<OptimizationChange>();
            foreach (var definition in definitions)
                changes.Add(await ApplyOneAsync(definition, sessionId, source, system, cancellationToken));
            var restart = StrongestRestart(changes.Where(change => change.Result == TweakChangeResult.Applied).Select(change => change.Restart));
            var session = new OptimizationSession(sessionId, started, name, changes, changes.All(change => change.Result != TweakChangeResult.Failed),
                DateTimeOffset.UtcNow, restart, restorePointCreated, source);
            await _history.AddAsync(session, cancellationToken);
            await InvalidateScanCacheAsync(cancellationToken);
            return session;
        }
        finally { _executionGate.Release(); }
    }

    public async Task<OptimizationSession> RestoreAsync(IEnumerable<string> tweakIds, CancellationToken cancellationToken = default)
    {
        await _executionGate.WaitAsync(cancellationToken);
        try { return await RestoreInternalAsync(tweakIds, "Restore Selected Tweaks", "user-restore", cancellationToken); }
        finally { _executionGate.Release(); }
    }

    public async Task<OptimizationSession> RestoreSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        await _executionGate.WaitAsync(cancellationToken);
        try
        {
            var active = await _activeChanges.GetAllAsync(cancellationToken);
            var ids = active.Values.Where(change => change.SessionId == sessionId).OrderByDescending(change => change.AppliedAt).Select(change => change.TweakId);
            return await RestoreInternalAsync(ids, "Restore Optimization Session", "session-restore", cancellationToken);
        }
        finally { _executionGate.Release(); }
    }

    private async Task<OptimizationChange> ApplyOneAsync(TweakDefinition definition, Guid sessionId, string source, SystemSnapshot system, CancellationToken cancellationToken)
    {
        var access = await HasAccessAsync(definition, cancellationToken);
        if (!access) return Change(definition, "Unavailable", "Locked", false, "The required package or add-on is not owned.", TweakChangeResult.Skipped);
        var compatibility = CompatibilityScanner.CheckMetadata(definition, system);
        if (!compatibility.Supported) return Change(definition, "Unavailable", "Skipped", false, compatibility.Reason, TweakChangeResult.Skipped);
        if (!definition.Actionable || !_operations.TryGet(definition.Id, out var operation))
            return Change(definition, "Review", "No automatic change", true, null, TweakChangeResult.Skipped);
        compatibility = await operation.CheckCompatibilityAsync(system, cancellationToken);
        if (!compatibility.Supported) return Change(definition, "Unavailable", "Skipped", false, compatibility.Reason, TweakChangeResult.Skipped);

        TweakValue? previous = null;
        try
        {
            var current = await operation.ReadAsync(system, cancellationToken);
            if (operation.IsDesired(current))
                return Change(definition, Serialize(current), Serialize(current), true, null, TweakChangeResult.AlreadyApplied, true);
            previous = await operation.BackupAsync(current, sessionId, cancellationToken);
            await operation.ApplyAsync(system, cancellationToken);
            var next = await operation.ReadAsync(system, cancellationToken);
            var verified = operation.IsDesired(next);
            await _activeChanges.UpsertAsync(new(definition.Id, sessionId, source, previous, next, DateTimeOffset.UtcNow, definition.Temporary, definition.Restart), cancellationToken);
            return verified
                ? Change(definition, Serialize(previous), Serialize(next), true, null, TweakChangeResult.Applied, true)
                : Change(definition, Serialize(previous), Serialize(next), false, "The requested value was not present after apply.", TweakChangeResult.Failed, false);
        }
        catch (Exception exception)
        {
            if (previous is not null)
            {
                try
                {
                    var observed = await operation.ReadAsync(system, cancellationToken);
                    if (!operation.IsRestored(observed, previous))
                        await _activeChanges.UpsertAsync(new(definition.Id, sessionId, source, previous, observed, DateTimeOffset.UtcNow, definition.Temporary, definition.Restart), cancellationToken);
                }
                catch { }
            }
            return Change(definition, previous is null ? "Read failed" : Serialize(previous), "Apply failed", false, exception.Message, TweakChangeResult.Failed);
        }
    }

    private async Task<OptimizationSession> RestoreInternalAsync(IEnumerable<string> tweakIds, string name, string source, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var sessionId = Guid.NewGuid();
        var system = await _systemInfo.ScanAsync(cancellationToken);
        var active = await _activeChanges.GetAllAsync(cancellationToken);
        var definitions = _catalog.GetAll().ToDictionary(definition => definition.Id, StringComparer.OrdinalIgnoreCase);
        var changes = new List<OptimizationChange>();
        foreach (var tweakId in tweakIds.Distinct(StringComparer.OrdinalIgnoreCase).Reverse())
        {
            if (!definitions.TryGetValue(tweakId, out var definition) || !active.TryGetValue(tweakId, out var captured) || !_operations.TryGet(tweakId, out var operation))
            {
                changes.Add(new(tweakId, definition?.Name ?? tweakId, "No active backup", "Skipped", true, null, TweakChangeResult.Skipped));
                continue;
            }
            try
            {
                await operation.RestoreAsync(captured.Previous, system, cancellationToken);
                var restored = await operation.ReadAsync(system, cancellationToken);
                var verified = operation.IsRestored(restored, captured.Previous);
                if (verified) await _activeChanges.RemoveAsync(tweakId, cancellationToken);
                changes.Add(Change(definition, Serialize(captured.Applied), Serialize(restored), verified,
                    verified ? null : "The captured previous state was not present after restore.", verified ? TweakChangeResult.Restored : TweakChangeResult.Failed, verified));
            }
            catch (Exception exception)
            {
                changes.Add(Change(definition, Serialize(captured.Applied), "Restore failed", false, exception.Message, TweakChangeResult.Failed));
            }
        }
        var session = new OptimizationSession(sessionId, started, name, changes, changes.All(change => change.Result != TweakChangeResult.Failed), DateTimeOffset.UtcNow,
            StrongestRestart(changes.Where(change => change.Result == TweakChangeResult.Restored).Select(change => change.Restart)), false, source);
        await _history.AddAsync(session, cancellationToken);
        await InvalidateScanCacheAsync(cancellationToken);
        return session;
    }

    private bool TryGetFreshScan(out IReadOnlyList<TweakListItem> scan)
    {
        scan = _scanCache!;
        return scan is not null && DateTimeOffset.UtcNow - _scanCachedAt < ScanCacheLifetime;
    }

    private async Task InvalidateScanCacheAsync(CancellationToken cancellationToken)
    {
        await _scanGate.WaitAsync(cancellationToken);
        try
        {
            _scanCache = null;
            _scanCachedAt = default;
        }
        finally { _scanGate.Release(); }
    }

    private IReadOnlyList<TweakDefinition> Resolve(IEnumerable<string> ids)
    {
        var selected = ids.Distinct(StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return _catalog.GetAll().Where(definition => selected.Contains(definition.Id)).ToList();
    }

    private async Task<bool> HasAccessAsync(TweakDefinition definition, CancellationToken cancellationToken)
    {
        var plan = await _entitlements.GetPlanAsync(cancellationToken);
        return plan >= definition.RequiredPlan || definition.RequiredEntitlement is not null && await _entitlements.HasEntitlementAsync(definition.RequiredEntitlement, cancellationToken);
    }

    private async Task<bool> TryCreateRestorePointAsync(string name, SystemSnapshot system, CancellationToken cancellationToken)
    {
        if (!_runner.IsWindows) return false;
        try
        {
            var safe = name.Replace("'", "''", StringComparison.Ordinal);
            await _runner.RunPowerShellAsync($"Checkpoint-Computer -Description 'Horizon - {safe[..Math.Min(60, safe.Length)]}' -RestorePointType MODIFY_SETTINGS", !system.IsAdministrator, TimeSpan.FromMinutes(3), cancellationToken);
            return true;
        }
        catch { return false; }
    }

    private static OptimizationChange Change(TweakDefinition definition, string previous, string next, bool success, string? error, TweakChangeResult result, bool verified = false) =>
        new(definition.Id, definition.Name, previous, next, success, error, result, verified, definition.Restart);
    private static string Serialize(TweakValue value) => JsonSerializer.Serialize(value, JsonOptions);
    private static RestartRequirement StrongestRestart(IEnumerable<RestartRequirement> requirements) => requirements.DefaultIfEmpty(RestartRequirement.None).Max();
}
