using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Zytter.Core.Data;
using Zytter.Core.Drafting;
using Zytter.Server.Features.Ai;
using Zytter.Server.Features.Battle;
using Zytter.Server.Hubs;

namespace Zytter.Server.Features.Drafting;

/// <summary>
/// 禁选驱动循环：4Hz 推进所有禁选房间，广播禁选事件；
/// 禁选完成后创建权威对局（沿用同一 roomId）并通知客户端进入战斗。
/// </summary>
public sealed class DraftLoopHost : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(250);

    private readonly DraftRegistry _drafts;
    private readonly BattleRegistry _battles;
    private readonly IHubContext<LobbyHub> _hub;
    private readonly AiDriver _ai;
    private readonly ILogger<DraftLoopHost> _logger;

    public DraftLoopHost(DraftRegistry drafts, BattleRegistry battles, IHubContext<LobbyHub> hub, AiDriver ai, ILogger<DraftLoopHost> logger)
    {
        _drafts = drafts;
        _battles = battles;
        _hub = hub;
        _ai = ai;
        _logger = logger;
    }

    public static string GroupName(Guid roomId) => $"draft-{roomId}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TickInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var draft in _drafts.All.ToList())
            {
                if (stoppingToken.IsCancellationRequested) return;

                try
                {
                    draft.Tick(TickInterval.TotalSeconds);

                    // 人机对战：轮到 AI 时驱动其禁用/选用/排序决策
                    if (_draftOwners.TryGetValue(draft.RoomId, out var owner)
                        && (owner.AccountA == BattleRoom.AiAccountId || owner.AccountB == BattleRoom.AiAccountId))
                    {
                        string aiSide = owner.AccountA == BattleRoom.AiAccountId ? "A" : "B";
                        _ai.TickDraft(draft, aiSide);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "禁选房间 {RoomId} 驱动异常", draft.RoomId);
                }

                var events = draft.DrainEvents();
                if (events.Count > 0)
                {
                    await _hub.Clients.Group(GroupName(draft.RoomId))
                        .SendAsync("DraftEvents", events, cancellationToken: stoppingToken);
                }

                // 禁选完成 → 创建权威对局（作废则不创建，客户端返回大厅）
                if (draft.IsCompleted && draft.Result is { } result)
                {
                    if (_draftOwners.TryGetValue(draft.RoomId, out var owner))
                    {
                        _battles.CreateWithId(draft.RoomId, owner.AccountA, owner.AccountB, result.RosterA, result.RosterB);
                        _logger.LogInformation("禁选 {RoomId} 完成，对局已创建：A={RosterA} B={RosterB}",
                            draft.RoomId, string.Join(",", result.RosterA), string.Join(",", result.RosterB));
                    }
                    _draftOwners.TryRemove(draft.RoomId, out _);
                    _drafts.Remove(draft.RoomId);
                }
                else if (draft.IsCompleted)
                {
                    _logger.LogInformation("禁选 {RoomId} 作废（某方未选出英雄）", draft.RoomId);
                    _draftOwners.TryRemove(draft.RoomId, out _);
                    _drafts.Remove(draft.RoomId);
                }
            }
        }
    }

    private readonly ConcurrentDictionary<Guid, (long AccountA, long AccountB)> _draftOwners = new();

    /// <summary>登记禁选房间归属（匹配时调用）。</summary>
    public void Register(Guid roomId, long accountA, long accountB)
    {
        _draftOwners[roomId] = (accountA, accountB);
    }

    /// <summary>查询禁选房间归属。</summary>
    public (long AccountA, long AccountB)? GetOwner(Guid roomId) =>
        _draftOwners.TryGetValue(roomId, out var owner) ? owner : null;
}
