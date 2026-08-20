using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Zytter.Core.Battle;
using Zytter.Core.Drafting;

namespace Zytter.Server.IntegrationTests;

/// <summary>
/// 单人练习（人机对战）集成测试：一个真实 SignalR 客户端扮演玩家，
/// 服务器侧 AI 自动完成 B 方禁选与对局行动。
/// 前置条件：Zytter.Server 已运行在 http://127.0.0.1:17717。
/// </summary>
public class AiMatchTests
{
    private const string ServerUrl = "http://127.0.0.1:17717";

    private static async Task<HubConnection> ConnectAsync(string hub)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl($"{ServerUrl}/hubs/{hub}", options => options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets)
            .AddJsonProtocol(options =>
            {
                options.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
            })
            .Build();
        await connection.StartAsync();
        return connection;
    }

    [Fact]
    public async Task AiMatchPlaysDraftAndBattle()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var lobby = await ConnectAsync("lobby");
        await using var lobbyD = lobby;

        var reg = await lobby.InvokeAsync<AuthResult>("Register", $"human_{suffix}", "password123");
        Assert.True(reg.Success, reg.Error);
        var login = await lobby.InvokeAsync<AuthResult>("Login", $"human_{suffix}", "password123");
        Assert.True(login.Success);

        // 1. 单人练习：直接返回 MatchFoundDto（对手为电脑，玩家恒为 A 方）
        var found = await lobby.InvokeAsync<MatchFoundDto>("EnqueueAiMatch", new EnqueueMatchRequest(login.Token!, new[] { 1, 2, 3 }));
        Assert.Equal("电脑", found.OpponentName);
        Assert.Equal("A", found.Side);

        // 2. 禁选：玩家只处理 A 方回合，B 方由服务器 AI 自动完成
        var removed = new HashSet<int>();
        var myPicks = new List<int>();
        var draftDone = new TaskCompletionSource<DraftCompletedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        lobby.On<DraftEvent[]>("DraftEvents", e =>
        {
            foreach (var ev in e)
            {
                switch (ev)
                {
                    case HeroBannedEvent ban when ban.HeroId != 0:
                        removed.Add(ban.HeroId);
                        break;
                    case HeroPickedEvent pick when pick.HeroId != 0:
                        removed.Add(pick.HeroId);
                        break;
                    case DraftStepChangedEvent { Side: "A" } step:
                        // 玩家从剩余英雄池中选第一个可用英雄
                        int heroId = Enumerable.Range(1, 12).First(id => !removed.Contains(id));
                        if (step.Kind == "pick") myPicks.Add(heroId);
                        _ = lobby.InvokeAsync(step.Kind == "ban" ? "DraftBan" : "DraftPick", found.RoomId, login.Token!, heroId);
                        break;
                    case DraftOrderPhaseEvent:
                        _ = lobby.InvokeAsync("DraftOrder", found.RoomId, login.Token!, myPicks.ToArray());
                        break;
                    case DraftCompletedEvent done:
                        draftDone.TrySetResult(done);
                        break;
                }
            }
            return Task.CompletedTask;
        });

        await lobby.InvokeAsync<DraftSnapshotDto>("DraftJoin", found.RoomId, login.Token!);
        var completed = await draftDone.Task.WaitAsync(TimeSpan.FromSeconds(120));
        Assert.True(completed.RosterA.Length > 0, "玩家应选出至少 1 名英雄");
        Assert.True(completed.RosterB.Length > 0, "AI 应自动完成选人");

        // 3. 对局：AI 应在行动阶段自动锁定行动（无需玩家驱动 B 方）
        var battle = await ConnectAsync("battle");
        await using var battleD = battle;
        var events = new ConcurrentQueue<BattleEvent>();
        battle.On<BattleEvent[]>("Events", e => { foreach (var x in e) events.Enqueue(x); });

        var snap = await JoinBattleWithRetryAsync(battle, found.RoomId, login.Token!);
        Assert.Equal(35, snap.RoundLimit);

        await WaitUntilAsync(() => events.Any(e => e is PhaseChangedEvent { Phase: BattlePhase.Action }), TimeSpan.FromSeconds(40));

        // 玩家普攻；AI 会自动锁定其行动
        await battle.InvokeAsync("SubmitAction", login.Token!, new ActionDto("attack"));
        await WaitUntilAsync(() => events.Any(e => e is ActionLockedEvent { Side: BattleSide.B }), TimeSpan.FromSeconds(20));

        // 回合应能正常结算（AI 侧产生了普攻或技能事件）
        await WaitUntilAsync(
            () => events.Any(e => e is BasicAttackEvent { Side: BattleSide.B } || e is SkillCastEvent { Side: BattleSide.B }),
            TimeSpan.FromSeconds(15));
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(200);
        }
        Assert.Fail($"等待条件超时（{timeout}）");
    }

    /// <summary>
    /// 带重试的加入对局：禁选完成事件先于服务器创建对局房间广播到达，
    /// 存在短暂窗口对局尚未就绪，重试直到成功。
    /// </summary>
    private static async Task<BattleSnapshotDto> JoinBattleWithRetryAsync(HubConnection battle, Guid roomId, string token)
    {
        Exception? last = null;
        for (int i = 0; i < 30; i++)
        {
            try
            {
                return await battle.InvokeAsync<BattleSnapshotDto>("JoinBattle", new JoinBattleRequest(roomId, token));
            }
            catch (Exception ex)
            {
                last = ex;
                await Task.Delay(200);
            }
        }
        throw last ?? new TimeoutException("加入对局超时");
    }
}
