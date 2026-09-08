using Horizon.Core.Interfaces;
using Horizon.Core.Models;

namespace Horizon.Restore;

public sealed class RestoreService : IRestoreService
{
    private readonly IHistoryService _history;
    private readonly ITweakEngine _engine;
    private readonly IActiveChangeStore _activeChanges;
    public RestoreService(IHistoryService history, ITweakEngine engine, IActiveChangeStore activeChanges) { _history = history; _engine = engine; _activeChanges = activeChanges; }

    public async Task<OptimizationSession?> UndoLastAsync(CancellationToken cancellationToken = default)
    {
        var active = await _activeChanges.GetAllAsync(cancellationToken);
        if (active.Count == 0) return null;
        var activeSessions = active.Values.Select(change => change.SessionId).ToHashSet();
        var last = (await _history.GetRecentAsync(500, cancellationToken)).FirstOrDefault(session => activeSessions.Contains(session.Id));
        return last is null ? null : await _engine.RestoreSessionAsync(last.Id, cancellationToken);
    }

    public Task<OptimizationSession> RestoreSessionAsync(Guid sessionId, CancellationToken cancellationToken = default) => _engine.RestoreSessionAsync(sessionId, cancellationToken);

    public async Task<OptimizationSession> RestoreAllAsync(CancellationToken cancellationToken = default)
    {
        var active = await _activeChanges.GetAllAsync(cancellationToken);
        return await _engine.RestoreAsync(active.Keys, cancellationToken);
    }
}
