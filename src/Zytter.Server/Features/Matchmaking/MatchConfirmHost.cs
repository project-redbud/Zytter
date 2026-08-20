using Microsoft.AspNetCore.SignalR;
using Zytter.Server.Hubs;

namespace Zytter.Server.Features.Matchmaking;

/// <summary>
/// 比赛确认倒计时后台循环：每 0.5 秒推进待确认比赛的接受窗口，
/// 超时未双方确认 → 向双方推送 MatchCancelled（整体取消，复刻原版 15 秒接受倒计时）。
/// </summary>
public sealed class MatchConfirmHost : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(500);

    private readonly MatchConfirmRegistry _registry;
    private readonly IHubContext<LobbyHub> _hub;
    private readonly ILogger<MatchConfirmHost> _logger;

    public MatchConfirmHost(MatchConfirmRegistry registry, IHubContext<LobbyHub> hub, ILogger<MatchConfirmHost> logger)
    {
        _registry = registry;
        _hub = hub;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TickInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var pending in _registry.All.ToList())
            {
                if (pending.Finished)
                {
                    _registry.Remove(pending.RoomId);
                    continue;
                }

                pending.RemainingSeconds -= TickInterval.TotalSeconds;
                if (pending.RemainingSeconds > 0) continue;

                pending.Finished = true;
                _logger.LogInformation("比赛确认超时取消：{RoomId}（{A} vs {B}）", pending.RoomId, pending.AName, pending.BName);
                await CancelPendingAsync(pending, "接受比赛超时，比赛已取消");
                _registry.Remove(pending.RoomId);
            }
        }
    }

    /// <summary>取消待确认比赛：向双方推送 MatchCancelled（任一放弃/超时即整体取消）。</summary>
    public async Task CancelPendingAsync(PendingMatchState pending, string reason)
    {
        var dto = new MatchCancelledDto(pending.RoomId, reason);
        foreach (var accountId in new[] { pending.AId, pending.BId })
        {
            if (LobbyHub.Connections.TryGetValue(accountId, out var connectionId))
            {
                await _hub.Clients.Client(connectionId).SendAsync("MatchCancelled", dto);
            }
        }
    }
}
