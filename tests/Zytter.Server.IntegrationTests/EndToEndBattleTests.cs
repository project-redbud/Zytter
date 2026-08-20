using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Zytter.Core.Battle;
using Zytter.Core.Drafting;

namespace Zytter.Server.IntegrationTests;

// 客户端侧 DTO（与服务端 Dtos.cs 的 JSON 形状一致，camelCase）
public sealed record AuthResult(bool Success, string? Error = null, string? Token = null, long? AccountId = null, string? Username = null);
public sealed record EnqueueMatchRequest(string Token, int[] Roster);
public sealed record MatchFoundDto(Guid RoomId, string Side, string OpponentName, long OpponentId, int[] Roster);
public sealed record MatchConfirmedDto(Guid RoomId);
public sealed record MatchCancelledDto(Guid RoomId, string Reason);
public sealed record JoinBattleRequest(Guid RoomId, string Token);
public sealed record BattleSnapshotDto(Guid RoomId, string Side, int Round, int RoundLimit, string Phase,
    double PhaseRemainingSeconds, string[] TeamA, string[] TeamB, int[] RosterA, int[] RosterB,
    string MyHeroName, int MyHeroId, int MyHp, int MyMaxHp, int MyMp, int MyMaxMp,
    string EnemyHeroName, int EnemyHeroId, int EnemyHp, int EnemyMaxHp, int EnemyMp, int EnemyMaxMp,
    long LastSeq);
public sealed record ActionDto(string Kind, string? Slot = null, int? ItemId = null, bool ChainQ = false);
public sealed record DraftSnapshotDto(Guid RoomId, string Side, string Phase, int StepIndex, double StepRemainingSeconds,
    int[] HeroPool, int[] BansA, int[] BansB, int[] PicksA, int[] PicksB);

/// <summary>
/// 端到端集成测试：两个真实 SignalR 客户端走完整流程
/// （注册 → 登录 → 匹配 → 加入对局 → 行动结算 → 掉线判负）。
/// 前置条件：Zytter.Server 已运行在 http://127.0.0.1:17717。
/// </summary>
public class EndToEndBattleTests
{
    private const string ServerUrl = "http://127.0.0.1:17717";

    private static async Task<HubConnection> ConnectAsync(string hub)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl($"{ServerUrl}/hubs/{hub}", options => options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets)
            .AddJsonProtocol(options =>
            {
                // 与服务端 PayloadSerializerOptions 保持一致（camelCase）
                options.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
            })
            .Build();
        await connection.StartAsync();
        return connection;
    }

    [Fact]
    public async Task RegisterLoginMatchAndPlayRounds()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var lobbyA = await ConnectAsync("lobby");
        var lobbyB = await ConnectAsync("lobby");
        await using var lobbyADisposable = lobbyA;
        await using var lobbyBDisposable = lobbyB;

        // 1. 注册 + 登录
        var regA = await lobbyA.InvokeAsync<AuthResult>("Register", $"alice_{suffix}", "password123");
        var regB = await lobbyB.InvokeAsync<AuthResult>("Register", $"bob_{suffix}", "password123");
        Assert.True(regA.Success, regA.Error);
        Assert.True(regB.Success, regB.Error);

        var loginA = await lobbyA.InvokeAsync<AuthResult>("Login", $"alice_{suffix}", "password123");
        Assert.True(loginA.Success);

        // 2. 匹配
        var matchA = new TaskCompletionSource<MatchFoundDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var matchB = new TaskCompletionSource<MatchFoundDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        lobbyA.On<MatchFoundDto>("MatchFound", m => matchA.TrySetResult(m));
        lobbyB.On<MatchFoundDto>("MatchFound", m => matchB.TrySetResult(m));

        Assert.True(await lobbyA.InvokeAsync<bool>("EnqueueMatch", new EnqueueMatchRequest(loginA.Token!, new[] { 1, 2, 3 })));
        Assert.True(await lobbyB.InvokeAsync<bool>("EnqueueMatch", new EnqueueMatchRequest(regB.Token!, new[] { 4, 5, 6 })));

        var foundA = await matchA.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var foundB = await matchB.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(foundA.RoomId, foundB.RoomId);
        Assert.NotEqual(foundA.Side, foundB.Side);
        Assert.True(foundA.OpponentId > 0);

        // 2.5 比赛确认：双方都接受后才创建禁选房间（复刻原版接受比赛流程）
        var confirmedA = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var confirmedB = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lobbyA.On<MatchConfirmedDto>("MatchConfirmed", m =>
        {
            if (m.RoomId == foundA.RoomId) confirmedA.TrySetResult(true);
            return Task.CompletedTask;
        });
        lobbyB.On<MatchConfirmedDto>("MatchConfirmed", m =>
        {
            if (m.RoomId == foundB.RoomId) confirmedB.TrySetResult(true);
            return Task.CompletedTask;
        });

        Assert.True(await lobbyA.InvokeAsync<bool>("AcceptMatch", foundA.RoomId, loginA.Token!));
        Assert.True(await lobbyB.InvokeAsync<bool>("AcceptMatch", foundB.RoomId, regB.Token!));
        await confirmedA.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await confirmedB.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // 2.5 禁选（B/P）：双方加入禁选房间，自动执行 BAN/PICK/排序直至完成
        var draftDoneA = new TaskCompletionSource<DraftCompletedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var draftDoneB = new TaskCompletionSource<DraftCompletedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        lobbyA.On<DraftEvent[]>("DraftEvents", e =>
        {
            foreach (var ev in e)
            {
                if (ev is DraftStepChangedEvent { Side: var side, Kind: var kind } step)
                {
                    // 自动：轮到谁就替谁操作（每步用不同英雄，避免重复禁/选）
                    int heroId = step.StepIndex + 1;
                    if (side == "A")
                        _ = lobbyA.InvokeAsync(kind == "ban" ? "DraftBan" : "DraftPick", foundA.RoomId, loginA.Token!, heroId);
                    else
                        _ = lobbyB.InvokeAsync(kind == "ban" ? "DraftBan" : "DraftPick", foundB.RoomId, regB.Token!, heroId);
                }
                else if (ev is DraftOrderPhaseEvent)
                {
                    _ = lobbyA.InvokeAsync("DraftOrder", foundA.RoomId, loginA.Token!, new[] { 5, 7, 9 });
                    _ = lobbyB.InvokeAsync("DraftOrder", foundB.RoomId, regB.Token!, new[] { 6, 8, 10 });
                }
                else if (ev is DraftCompletedEvent done)
                {
                    draftDoneA.TrySetResult(done);
                    draftDoneB.TrySetResult(done);
                }
            }
            return Task.CompletedTask;
        });

        var draftSnapA = await lobbyA.InvokeAsync<DraftSnapshotDto>("DraftJoin", foundA.RoomId, loginA.Token!);
        var draftSnapB = await lobbyB.InvokeAsync<DraftSnapshotDto>("DraftJoin", foundB.RoomId, regB.Token!);
        Assert.Equal(12, draftSnapA.HeroPool.Length);

        var completed = await draftDoneA.Task.WaitAsync(TimeSpan.FromSeconds(120));
        Assert.True(completed.RosterA.Length > 0);
        Assert.True(completed.RosterB.Length > 0);

        // 3. 加入对局
        var battleA = await ConnectAsync("battle");
        var battleB = await ConnectAsync("battle");
        await using var ___ = battleA;
        await using var ____ = battleB;

        var eventsA = new ConcurrentQueue<BattleEvent>();
        var eventsB = new ConcurrentQueue<BattleEvent>();
        battleA.On<BattleEvent[]>("Events", e => { foreach (var x in e) eventsA.Enqueue(x); });
        battleB.On<BattleEvent[]>("Events", e => { foreach (var x in e) eventsB.Enqueue(x); });

        var snapA = await battleA.InvokeAsync<BattleSnapshotDto>("JoinBattle", new JoinBattleRequest(foundA.RoomId, loginA.Token!));
        var snapB = await battleB.InvokeAsync<BattleSnapshotDto>("JoinBattle", new JoinBattleRequest(foundB.RoomId, regB.Token!));
        Assert.Equal(35, snapA.RoundLimit);
        Assert.Equal(3, snapA.RosterA.Length);

        // 4. 等待第一回合行动阶段（热身 20 秒 + 准备 3 秒）
        await WaitUntilAsync(() => eventsA.Any(e => e is RoundStartedEvent), TimeSpan.FromSeconds(35));
        await WaitUntilAsync(() => eventsA.Any(e => e is PhaseChangedEvent { Phase: BattlePhase.Action }), TimeSpan.FromSeconds(40));

        // 5. 双方普攻 → 应产生伤害事件与回合结束金币事件
        await battleA.InvokeAsync("SubmitAction", loginA.Token!, new ActionDto("attack"));
        await battleB.InvokeAsync("SubmitAction", regB.Token!, new ActionDto("attack"));

        await WaitUntilAsync(() => eventsA.Any(e => e is DamageDealtEvent), TimeSpan.FromSeconds(15));
        await WaitUntilAsync(() => eventsB.Any(e => e is DamageDealtEvent), TimeSpan.FromSeconds(15));
        await WaitUntilAsync(() => eventsA.Any(e => e is GoldChangedEvent), TimeSpan.FromSeconds(15));

        // 双方都收到了伤害事件（一致性）
        Assert.Contains(eventsA, e => e is DamageDealtEvent);
        Assert.Contains(eventsB, e => e is DamageDealtEvent);

        // 6. B 掉线 → A 收到 BattleEnded（Disconnect 判胜）
        await battleB.DisposeAsync();
        await WaitUntilAsync(() => eventsA.Any(e => e is BattleEndedEvent), TimeSpan.FromSeconds(15));

        var result = (BattleEndedEvent)eventsA.First(e => e is BattleEndedEvent);
        Assert.Equal(VictoryReason.Disconnect, result.Reason);
        Assert.Equal(BattleSide.A, result.Winner);
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
}
