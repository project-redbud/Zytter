using System.Text.Json.Serialization;
using Zytter.Core.Battle;
using Zytter.Core.Common;
using Zytter.Core.Data;

namespace Zytter.Server.Features.Battle;

/// <summary>
/// 服务器侧对局房间：持有权威 BattleSession，缓冲事件流并派发给客户端。
/// 客户端通过 SignalR 提交命令（强类型 DTO → Core 命令），
/// 服务器校验后执行，事件经 Hub 广播给双方。
/// </summary>
public sealed class BattleRoom
{
    /// <summary>AI 对手的保留账户 ID（真实账户自增 ID 从 1 起，0 恒为空闲哨兵值）。</summary>
    public const long AiAccountId = 0;

    public required Guid Id { get; init; }
    public required long AccountA { get; init; }
    public required long AccountB { get; init; }
    public required BattleSession Session { get; init; }

    /// <summary>是否为单人练习（人机对战）。</summary>
    public bool IsAi => AccountA == AiAccountId || AccountB == AiAccountId;

    /// <summary>AI 一方（若为非 AI 对战，返回 A）。</summary>
    public BattleSide AiSide => AccountA == AiAccountId ? BattleSide.A : BattleSide.B;

    private readonly List<BattleEvent> _pendingEvents = new();
    private readonly object _lock = new();
    private readonly object _gate = new(); // BattleSession 单线程确定性模型 → 命令与 Tick 串行化

    public DateTime? FinishedAt { get; private set; }

    /// <summary>在房间门锁内操作会话（命令执行与 Tick 共用此锁保证线程安全）。</summary>
    public void WithSession(Action<BattleSession> action)
    {
        lock (_gate)
        {
            action(Session);
            if (Session.IsFinished && FinishedAt is null)
                FinishedAt = DateTime.UtcNow;
        }
    }

    /// <summary>取出并清空待广播事件。</summary>
    public IReadOnlyList<BattleEvent> DrainEvents()
    {
        lock (_lock)
        {
            if (_pendingEvents.Count == 0) return Array.Empty<BattleEvent>();
            var snapshot = _pendingEvents.ToArray();
            _pendingEvents.Clear();
            return snapshot;
        }
    }

    /// <summary>引擎产出的事件入缓冲（由 BattleSession.Emit 钩入）。</summary>
    public void Capture(BattleEvent e)
    {
        lock (_lock)
        {
            _pendingEvents.Add(e);
        }
    }

    /// <summary>执行客户端命令并捕获事件。</summary>
    public void Execute(BattleCommand command) => Session.Execute(command);

    public long GetAccount(BattleSide side) => side == BattleSide.A ? AccountA : AccountB;

    public BattleSide? GetSide(long accountId) =>
        accountId == AccountA ? BattleSide.A : accountId == AccountB ? BattleSide.B : null;
}
