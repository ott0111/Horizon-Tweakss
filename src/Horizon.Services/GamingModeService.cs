using System.Diagnostics;
using System.Text.Json;
using Horizon.Core.Interfaces;
using Horizon.Core.Models;

namespace Horizon.Services;

public sealed class GamingModeService : IGamingModeService
{
    private static string StatePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Horizon", "gaming-mode.json");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly IReadOnlyDictionary<string, string[]> Processes = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["fortnite"] = ["FortniteClient-Win64-Shipping", "FortniteLauncher"],
        ["valorant"] = ["VALORANT-Win64-Shipping"],
        ["callOfDuty"] = ["cod", "ModernWarfare"],
        ["apex"] = ["r5apex"],
        ["cs2"] = ["cs2"],
        ["rocketLeague"] = ["RocketLeague"]
    };

    private readonly ITweakEngine _engine;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _lifetime;
    private Task? _monitor;
    public event EventHandler<GamingModeStatus>? StatusChanged;

    public GamingModeService(ITweakEngine engine) => _engine = engine;

    public async Task<GamingModeStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var state = await ReadStateAsync(cancellationToken);
        return ToStatus(state);
    }

    public async Task ConfigureAsync(string gameId, bool enabled, IEnumerable<string> temporaryTweakIds, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await ReadStateUnsafeAsync(cancellationToken);
            state.Enabled = enabled || state.Profiles.Any(pair => pair.Key != gameId && pair.Value.Enabled);
            state.Profiles[gameId] = new(enabled, temporaryTweakIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
            if (!enabled && state.ActiveGame?.Equals(gameId, StringComparison.OrdinalIgnoreCase) == true) await RestoreActiveUnsafeAsync(state, cancellationToken);
            await WriteStateUnsafeAsync(state, cancellationToken);
            Raise(state);
        }
        finally { _gate.Release(); }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows() || _monitor is not null) return Task.CompletedTask;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var lifetimeToken = _lifetime.Token;
        _monitor = Task.Run(() => MonitorAsync(lifetimeToken), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_lifetime is null) return;
        _lifetime.Cancel();
        try { if (_monitor is not null) await _monitor.WaitAsync(cancellationToken); } catch (OperationCanceledException) { }
        _lifetime.Dispose(); _lifetime = null; _monitor = null;
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(4));
        do
        {
            try { await TickAsync(cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                StatusChanged?.Invoke(this, new(false, null, null, null, null, exception.Message));
            }
        } while (await timer.WaitForNextTickAsync(cancellationToken));
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await ReadStateUnsafeAsync(cancellationToken);
            if (state.ActiveGame is not null)
            {
                if (!IsRunning(state.ActiveGame))
                {
                    await RestoreActiveUnsafeAsync(state, cancellationToken);
                    await WriteStateUnsafeAsync(state, cancellationToken);
                    Raise(state);
                }
                return;
            }
            if (!state.Enabled) return;
            foreach (var profile in state.Profiles.Where(pair => pair.Value.Enabled))
            {
                if (!IsRunning(profile.Key)) continue;
                var session = await _engine.ApplySessionAsync(profile.Value.TweakIds, $"{profile.Key} Gaming Mode", "gaming-mode", cancellationToken);
                var appliedIds = session.Changes.Where(change => change.Result == TweakChangeResult.Applied).Select(change => change.TweakId).ToArray();
                state.ActiveGame = profile.Key; state.ActiveSessionId = session.Id; state.ActiveTweakIds = appliedIds; state.StartedAt = DateTimeOffset.UtcNow; state.Error = session.Success ? null : "One or more temporary operations failed.";
                await WriteStateUnsafeAsync(state, cancellationToken);
                Raise(state);
                break;
            }
        }
        finally { _gate.Release(); }
    }

    private async Task RestoreActiveUnsafeAsync(GamingState state, CancellationToken cancellationToken)
    {
        if (state.ActiveTweakIds.Length > 0) await _engine.RestoreAsync(state.ActiveTweakIds, cancellationToken);
        state.ActiveGame = null; state.ActiveSessionId = null; state.ActiveTweakIds = []; state.StartedAt = null; state.LastRestoredAt = DateTimeOffset.UtcNow; state.Error = null;
    }

    private static bool IsRunning(string gameId)
    {
        if (!Processes.TryGetValue(gameId, out var names)) return false;
        foreach (var name in names)
        {
            try { if (Process.GetProcessesByName(name).Length > 0) return true; } catch { }
        }
        return false;
    }

    private async Task<GamingState> ReadStateAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return await ReadStateUnsafeAsync(cancellationToken); }
        finally { _gate.Release(); }
    }
    private static async Task<GamingState> ReadStateUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(StatePath)) return new();
        try
        {
            await using var stream = File.OpenRead(StatePath);
            return await JsonSerializer.DeserializeAsync<GamingState>(stream, JsonOptions, cancellationToken) ?? new();
        }
        catch (JsonException) { return new(); }
    }
    private static async Task WriteStateUnsafeAsync(GamingState state, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
        var temporary = StatePath + ".tmp";
        await using (var stream = File.Create(temporary)) await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
        File.Move(temporary, StatePath, true);
    }
    private void Raise(GamingState state) => StatusChanged?.Invoke(this, ToStatus(state));
    private static GamingModeStatus ToStatus(GamingState state) => new(state.Enabled, state.ActiveGame, state.ActiveSessionId, state.StartedAt, state.LastRestoredAt, state.Error);
    public async ValueTask DisposeAsync() { await StopAsync(); _gate.Dispose(); }

    private sealed class GamingState
    {
        public bool Enabled { get; set; }
        public Dictionary<string, GamingProfileState> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string? ActiveGame { get; set; }
        public Guid? ActiveSessionId { get; set; }
        public string[] ActiveTweakIds { get; set; } = [];
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? LastRestoredAt { get; set; }
        public string? Error { get; set; }
    }
    private sealed record GamingProfileState(bool Enabled, string[] TweakIds);
}
