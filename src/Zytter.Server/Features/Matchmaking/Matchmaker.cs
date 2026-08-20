using System.Collections.Concurrent;
using Zytter.Core.Battle;

namespace Zytter.Server.Features.Matchmaking;

/// <summary>匹配队列条目。</summary>
public sealed record MatchQueueEntry(long AccountId, IReadOnlyList<int> Roster);

/// <summary>
/// 1v1 匹配器：FIFO 队列，凑齐两人即开局（MVP 阶段不带 ELO 窗口，
/// 原版的 elo 窗口 400 起每 30 轮 +400 可在后续加入）。
/// </summary>
public sealed class Matchmaker
{
    private readonly ConcurrentQueue<MatchQueueEntry> _queue = new();
    private readonly object _lock = new();

    public int QueueLength => _queue.Count;

    public bool Enqueue(MatchQueueEntry entry)
    {
        // 已在队列中则拒绝重复入队
        if (_queue.Any(e => e.AccountId == entry.AccountId))
            return false;
        _queue.Enqueue(entry);
        return true;
    }

    public void Cancel(long accountId)
    {
        lock (_lock)
        {
            var remaining = _queue.Where(e => e.AccountId != accountId).ToList();
            _queue.Clear();
            foreach (var e in remaining) _queue.Enqueue(e);
        }
    }

    /// <summary>尝试撮合：队列 ≥2 时取出两人。返回 null 表示人数不足。</summary>
    public (MatchQueueEntry A, MatchQueueEntry B)? TryMatch()
    {
        lock (_lock)
        {
            if (!_queue.TryDequeue(out var a) || !_queue.TryDequeue(out var b))
            {
                // 只取出一人则放回
                if (a is not null) _queue.Enqueue(a);
                return null;
            }
            return (a, b);
        }
    }
}

/// <summary>匹配结果（由 LobbyHub 推送给双方）。</summary>
public sealed record MatchFoundResult(Guid RoomId, BattleSide Side, string OpponentName);
