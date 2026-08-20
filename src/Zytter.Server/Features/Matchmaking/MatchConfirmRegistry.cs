using System.Collections.Concurrent;

namespace Zytter.Server.Features.Matchmaking;

/// <summary>
/// 待确认比赛状态（匹配成功 → 双方各自确认 → 都接受才开禁选；任一放弃/超时则整体取消）。
/// 复刻原版 Match.java 的"接受比赛倒计时"流程，双方都确认后才进入 B/P。
/// </summary>
public sealed class PendingMatchState
{
    public required Guid RoomId { get; init; }
    public required long AId { get; init; }
    public required long BId { get; init; }
    public required string AName { get; init; }
    public required string BName { get; init; }

    /// <summary>接受窗口剩余秒数（15 秒，超时视为放弃）。</summary>
    public double RemainingSeconds { get; set; } = 15;

    public bool AAccepted { get; set; }
    public bool BAccepted { get; set; }

    /// <summary>防重复结算（接受/取消只能执行一次）。</summary>
    public bool Finished { get; set; }

    public string NameOf(long accountId) => accountId == AId ? AName : BName;
}

/// <summary>待确认比赛注册表。</summary>
public sealed class MatchConfirmRegistry
{
    private readonly ConcurrentDictionary<Guid, PendingMatchState> _pending = new();

    public PendingMatchState Add(Guid roomId, long aId, long bId, string aName, string bName)
    {
        var state = new PendingMatchState
        {
            RoomId = roomId,
            AId = aId,
            BId = bId,
            AName = aName,
            BName = bName,
        };
        _pending[roomId] = state;
        return state;
    }

    public PendingMatchState? Get(Guid roomId) => _pending.GetValueOrDefault(roomId);

    public void Remove(Guid roomId) => _pending.TryRemove(roomId, out _);

    public ICollection<PendingMatchState> All => _pending.Values;
}
