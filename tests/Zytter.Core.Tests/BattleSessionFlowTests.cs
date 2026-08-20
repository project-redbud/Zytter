using System.Text.Json;
using Zytter.Core.Battle;
using Zytter.Core.Common;
using Zytter.Core.Data;

namespace Zytter.Core.Tests;

/// <summary>战斗引擎测试公共工具。</summary>
internal static class BattleTestHelper
{
    public static GameDataCatalog Catalog { get; } = GameDataCatalog.LoadDefault();

    public static BattleSession CreateSession(
        ulong seed = 42,
        int[]? rosterA = null,
        int[]? rosterB = null,
        BattleConfig? config = null)
    {
        var session = new BattleSession(
            Catalog,
            config ?? new BattleConfig(),
            new SeededRng(seed),
            (rosterA ?? new[] { 1, 2, 3 }).Select(Catalog.GetHero).ToList(),
            (rosterB ?? new[] { 4, 5, 6 }).Select(Catalog.GetHero).ToList());
        session.Start();
        return session;
    }

    /// <summary>快进指定秒数（小步长避免跨阶段）。</summary>
    public static void Tick(BattleSession session, double seconds)
    {
        const double step = 0.25;
        double remaining = seconds;
        while (remaining > 0 && !session.IsFinished)
        {
            session.Tick(Math.Min(step, remaining));
            remaining -= step;
        }
    }

    /// <summary>推进到指定战斗回合的行动阶段（逐步推进，避免跨阶段竞态）。</summary>
    public static void EnterActionPhase(BattleSession session, int round)
    {
        while (!session.IsFinished && !(session.Round == round && session.Phase == BattlePhase.Action))
            Tick(session, 0.25);
    }

    /// <summary>双方提交行动并结算本回合（结算 + 回合结束阶段）。</summary>
    public static void PlayRound(BattleSession session, PlayerAction actionA, PlayerAction actionB)
    {
        session.Execute(new SubmitActionCommand(BattleSide.A, actionA));
        session.Execute(new SubmitActionCommand(BattleSide.B, actionB));
        Tick(session, 5.01); // 兵戎相见（5 秒）+ 偃革倒戈
    }
}

public class BattleSessionFlowTests
{
    [Fact]
    public void WarmupThenFirstRound()
    {
        var session = BattleTestHelper.CreateSession();
        Assert.Equal(BattlePhase.Warmup, session.Phase);

        BattleTestHelper.Tick(session, 20);
        Assert.Equal(BattlePhase.Prepare, session.Phase);
        Assert.Equal(1, session.Round);
    }

    [Fact]
    public void ActionPhaseTimeoutAutoSkips()
    {
        var session = BattleTestHelper.CreateSession();
        BattleTestHelper.Tick(session, 20);   // 热身
        BattleTestHelper.Tick(session, 3);    // 准备
        Assert.Equal(BattlePhase.Action, session.Phase);

        BattleTestHelper.Tick(session, 30);   // 双方超时 → 自动放弃
        Assert.Contains(session.Log, e => e is ActionSkippedEvent { Reason: "timeout" });
        Assert.True(session.Round == 1);
    }

    [Fact]
    public void BothSubmitEndsActionPhaseEarly()
    {
        var session = BattleTestHelper.CreateSession();
        BattleTestHelper.Tick(session, 20);
        BattleTestHelper.Tick(session, 3);

        session.Execute(new SubmitActionCommand(BattleSide.A, new SkipAction()));
        session.Execute(new SubmitActionCommand(BattleSide.B, new SkipAction()));
        Assert.Equal(BattlePhase.Resolving, session.Phase);
    }

    [Fact]
    public void RoundEndGrantsGold()
    {
        var session = BattleTestHelper.CreateSession();
        BattleTestHelper.Tick(session, 20);
        BattleTestHelper.Tick(session, 3);

        int goldA = session.PlayerA.Wallet.Gold;
        BattleTestHelper.PlayRound(session, new SkipAction(), new SkipAction());
        Assert.Equal(goldA + 1, session.PlayerA.Wallet.Gold);
    }

    [Fact]
    public void ShopRoundGrantsTwoGoldAndOpensShop()
    {
        var session = BattleTestHelper.CreateSession();
        BattleTestHelper.EnterActionPhase(session, 5);
        BattleTestHelper.PlayRound(session, new SkipAction(), new SkipAction()); // 第5回合 → nextround 6 商店

        Assert.Equal(BattlePhase.Shop, session.Phase);
        Assert.True(session.PlayerA.Wallet.Gold >= 2);
    }

    [Fact]
    public void RoundExtendItemIncreasesLimit()
    {
        var session = BattleTestHelper.CreateSession();
        BattleTestHelper.EnterActionPhase(session, 5);
        BattleTestHelper.PlayRound(session, new SkipAction(), new SkipAction());
        Assert.Equal(BattlePhase.Shop, session.Phase);

        int before = session.RoundLimit;
        session.Execute(new ShopPurchaseCommand(BattleSide.A, 1));
        Assert.Equal(before + 5, session.RoundLimit);

        // 限购 1 次
        Assert.Throws<RuleViolationException>(() => session.Execute(new ShopPurchaseCommand(BattleSide.A, 1)));
    }

    [Fact]
    public void SurrenderRequiresRound13()
    {
        var session = BattleTestHelper.CreateSession();
        Assert.Throws<RuleViolationException>(() => session.Execute(new SurrenderCommand(BattleSide.A)));

        BattleTestHelper.EnterActionPhase(session, 13);
        session.Execute(new SurrenderCommand(BattleSide.A));
        Assert.True(session.IsFinished);
        Assert.Equal(BattleSide.B, session.Winner);
        Assert.Equal(VictoryReason.Surrender, session.WinReason);
    }
}

public class BattleEngineDeterminismTests
{
    [Fact]
    public void SameSeedProducesIdenticalEventStreams()
    {
        var s1 = BattleTestHelper.CreateSession(seed: 1234);
        var s2 = BattleTestHelper.CreateSession(seed: 1234);

        // 打 10 个回合：双方都用普攻（消耗随机数，验证确定性）
        for (int i = 0; i < 10 && !s1.IsFinished && !s2.IsFinished; i++)
        {
            BattleTestHelper.EnterActionPhase(s1, i + 1);
            BattleTestHelper.EnterActionPhase(s2, i + 1);
            var a1 = new BasicAttackAction();
            var b1 = new BasicAttackAction();
            s1.Execute(new SubmitActionCommand(BattleSide.A, a1));
            s1.Execute(new SubmitActionCommand(BattleSide.B, b1));
            s2.Execute(new SubmitActionCommand(BattleSide.A, a1));
            s2.Execute(new SubmitActionCommand(BattleSide.B, b1));
            BattleTestHelper.Tick(s1, 5.01);
            BattleTestHelper.Tick(s2, 5.01);
        }

        Assert.Equal(s1.Log.Count, s2.Log.Count);
        for (int i = 0; i < s1.Log.Count; i++)
        {
            // 用 JSON 序列化比较：BuffSyncEvent 的 Rounds 字典无引用相等语义，按传输格式比对更贴近真实确定性
            string j1 = JsonSerializer.Serialize(s1.Log[i], s1.Log[i].GetType());
            string j2 = JsonSerializer.Serialize(s2.Log[i], s2.Log[i].GetType());
            Assert.Equal(j1, j2);
        }
    }
}
