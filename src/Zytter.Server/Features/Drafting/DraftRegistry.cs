using System.Collections.Concurrent;
using Zytter.Core.Drafting;

namespace Zytter.Server.Features.Drafting;

/// <summary>禁选房间注册表（内存态）。</summary>
public sealed class DraftRegistry
{
    private readonly ConcurrentDictionary<Guid, DraftSession> _sessions = new();

    public DraftSession? Get(Guid roomId) => _sessions.GetValueOrDefault(roomId);

    public DraftSession Create(Guid roomId, IReadOnlyList<int> heroPool)
    {
        var session = new DraftSession(roomId, heroPool);
        _sessions[roomId] = session;
        return session;
    }

    public void Remove(Guid roomId) => _sessions.TryRemove(roomId, out _);

    public ICollection<DraftSession> All => _sessions.Values;
}
