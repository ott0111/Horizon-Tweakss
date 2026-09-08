using System.Text.Json;
using Horizon.Core.Interfaces;
using Horizon.Core.Models;

namespace Horizon.Infrastructure.Persistence;

public sealed class JsonActiveChangeStore : IActiveChangeStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IReadOnlyDictionary<string, ActiveTweakChange>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return new Dictionary<string, ActiveTweakChange>(await ReadUnsafeAsync(cancellationToken), StringComparer.OrdinalIgnoreCase); }
        finally { _gate.Release(); }
    }

    public async Task<ActiveTweakChange?> GetAsync(string tweakId, CancellationToken cancellationToken = default)
    {
        var values = await GetAllAsync(cancellationToken);
        return values.GetValueOrDefault(tweakId);
    }

    public async Task UpsertAsync(ActiveTweakChange change, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var values = await ReadUnsafeAsync(cancellationToken);
            values[change.TweakId] = change;
            await WriteUnsafeAsync(values, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task RemoveAsync(string tweakId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var values = await ReadUnsafeAsync(cancellationToken);
            if (values.Remove(tweakId)) await WriteUnsafeAsync(values, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    private static async Task<Dictionary<string, ActiveTweakChange>> ReadUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(LocalPaths.ActiveChanges)) return new(StringComparer.OrdinalIgnoreCase);
        try
        {
            await using var stream = File.OpenRead(LocalPaths.ActiveChanges);
            var values = await JsonSerializer.DeserializeAsync<Dictionary<string, ActiveTweakChange>>(stream, Options, cancellationToken);
            return values is null ? new(StringComparer.OrdinalIgnoreCase) : new(values, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            var corrupt = LocalPaths.ActiveChanges + $".corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
            File.Move(LocalPaths.ActiveChanges, corrupt, true);
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static async Task WriteUnsafeAsync(Dictionary<string, ActiveTweakChange> values, CancellationToken cancellationToken)
    {
        var temporary = LocalPaths.ActiveChanges + ".tmp";
        await using (var stream = File.Create(temporary)) await JsonSerializer.SerializeAsync(stream, values, Options, cancellationToken);
        File.Move(temporary, LocalPaths.ActiveChanges, true);
    }
}
