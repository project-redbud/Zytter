using Microsoft.AspNetCore.SignalR;
using Zytter.Server.Features.Ai;
using Zytter.Server.Features.Battle;
using Zytter.Server.Hubs;

namespace Zytter.Server.Features.Battle;

/// <summary>
/// 对局驱动循环：按 4Hz 推进所有活动房间的引擎时钟，
/// 并把事件流广播给房间内的客户端（SignalR 组）。
/// 对局结束后保留 60 秒供客户端读取结果，随后清理。
/// </summary>
public sealed class BattleLoopHost : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan FinishedRetention = TimeSpan.FromSeconds(60);

    private readonly BattleRegistry _registry;
    private readonly IHubContext<BattleHub> _hub;
    private readonly MatchRecorder _recorder;
    private readonly AiDriver _ai;
    private readonly ILogger<BattleLoopHost> _logger;

    public BattleLoopHost(BattleRegistry registry, IHubContext<BattleHub> hub, MatchRecorder recorder, AiDriver ai, ILogger<BattleLoopHost> logger)
    {
        _registry = registry;
        _hub = hub;
        _recorder = recorder;
        _ai = ai;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TickInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var room in _registry.All)
            {
                if (stoppingToken.IsCancellationRequested) return;

                try
                {
                    room.WithSession(s =>
                    {
                        s.Tick(TickInterval.TotalSeconds);

                        // 人机对战：驱动 AI 侧决策（与玩家命令共用房间门锁，串行安全）
                        if (room.IsAi && !s.IsFinished)
                            _ai.TickBattle(s, room.AiSide, room.Id);
                    });

                    var events = room.DrainEvents();
                    if (events.Count > 0)
                    {
                        await _hub.Clients.Group(GroupName(room.Id))
                            .SendAsync("Events", events, cancellationToken: stoppingToken);
                    }

                    if (room.FinishedAt is { } finishedAt && DateTime.UtcNow - finishedAt > FinishedRetention)
                    {
                        _registry.Remove(room.Id);
                        _ai.Forget(room.Id);

                        // 人机练习不计入天梯/战绩（不污染段位与排行）
                        if (!room.IsAi)
                        {
                            try
                            {
                                await _recorder.RecordAsync(room);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "对局 {RoomId} 结算落库失败", room.Id);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "对局房间 {RoomId} 驱动异常", room.Id);
                }
            }
        }
    }

    public static string GroupName(Guid roomId) => $"battle-{roomId}";
}
