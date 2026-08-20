using Zytter.Core.Battle;

namespace Zytter.Core.Tests;

/// <summary>回归测试：修复"红月升级静默卸装"与"属性不同步"两类缺陷。</summary>
public class SyncAndUpgradeTests
{
    [Fact]
    public void RedMoonUpgradeFromWornSlotEmitsRemovalEvents()
    {
        var session = BattleTestHelper.CreateSession();

        // 第 5 回合结束 → 第 6 商店回合：买红月神杖
        BattleTestHelper.EnterActionPhase(session, 5);
        BattleTestHelper.PlayRound(session, new SkipAction(), new SkipAction());
        Assert.Equal(BattlePhase.Shop, session.Phase);
        session.Execute(new ShopPurchaseCommand(BattleSide.A, 14));
        Assert.True(session.PlayerA.Box.Contains(14));

        // 商店结束进入第 6 战斗回合：穿戴红月到 Z 槽
        BattleTestHelper.Tick(session, 25); // 商店关闭 → 准备 → 行动
        Assert.Equal(BattlePhase.Action, session.Phase);
        session.Execute(new EquipCommand(BattleSide.A, EquipmentSlot.Z, 14));
        Assert.True(session.PlayerA.Current!.Equipment.IsWorn(14));

        // 推进到第 13 商店回合：再买红月 → 升级二阶
        while (!session.IsFinished && session.Phase != BattlePhase.Shop)
            BattleTestHelper.Tick(session, 0.25);
        Assert.Equal(BattlePhase.Shop, session.Phase);
        session.Execute(new ShopPurchaseCommand(BattleSide.A, 14));

        Assert.False(session.PlayerA.Current.Equipment.IsWorn(14), "升级后原红月应被卸下");
        Assert.True(session.PlayerA.Box.Contains(27), "二阶红月应进入道具盒");
        Assert.False(session.PlayerA.Box.Contains(14), "原红月不应留在道具盒");

        Assert.Contains(session.Log, e => e is EquipmentChangedEvent { Slot: "Z", ItemId: null });
        Assert.Contains(session.Log, e => e is ItemLostEvent { ItemId: 14 });
        Assert.Contains(session.Log, e => e is ItemObtainedEvent { ItemId: 27 });
    }

    [Fact]
    public void RoundEndEmitsHeroStatsSyncForBothSides()
    {
        var session = BattleTestHelper.CreateSession();
        BattleTestHelper.EnterActionPhase(session, 1);
        BattleTestHelper.PlayRound(session, new SkipAction(), new SkipAction());

        var syncA = session.Log.OfType<HeroStatsSyncEvent>().First(e => e.Side == BattleSide.A);
        var syncB = session.Log.OfType<HeroStatsSyncEvent>().First(e => e.Side == BattleSide.B);

        Assert.Equal(session.PlayerA.Current!.Stats.Hp, syncA.Hp);
        Assert.Equal(session.PlayerA.Current.Stats.MaxHp, syncA.MaxHp);
        Assert.Equal(session.PlayerA.Current.Hero.Name, syncA.HeroName);
        Assert.Equal(session.PlayerB.Current!.Stats.Hp, syncB.Hp);
    }

    [Fact]
    public void SecondDeathBringsInThirdHeroWithFullHp()
    {
        var session = BattleTestHelper.CreateSession(rosterA: new[] { 1, 2, 3 }, rosterB: new[] { 4, 5, 6 });
        BattleTestHelper.EnterActionPhase(session, 1);

        // 第一回合：处死 A 的第一名英雄 → 结算时应换上第二名（不得跳过）
        session.PlayerA.Current!.Stats.AddHp(-session.PlayerA.Current.Stats.Hp);
        BattleTestHelper.PlayRound(session, new SkipAction(), new SkipAction());
        Assert.Equal(2, session.PlayerA.Current!.Hero.Id);

        // 第二回合：处死 A 的第二名英雄 → 第三名上场且满血满蓝
        BattleTestHelper.EnterActionPhase(session, 2);
        string secondName = session.PlayerA.Current.Hero.Name;
        session.PlayerA.Current.Stats.AddHp(-session.PlayerA.Current.Stats.Hp);
        BattleTestHelper.PlayRound(session, new SkipAction(), new SkipAction());

        Assert.Equal(3, session.PlayerA.Current.Hero.Id);
        Assert.False(session.IsFinished, "第三名英雄上场后对局不应结束");
        Assert.Equal(session.PlayerA.Current.Stats.MaxHp, session.PlayerA.Current.Stats.Hp);
        Assert.Equal(session.PlayerA.Current.Stats.MaxMp, session.PlayerA.Current.Stats.Mp);

        // 事件流：第二次阵亡后跟着换人事件与属性同步
        var events = session.Log;
        int secondDeathIdx = events.ToList().FindLastIndex(e => e is HeroDiedEvent);
        Assert.Contains(events.Skip(secondDeathIdx), e => e is HeroSwitchedEvent { HeroName: var n } && n != secondName);
        Assert.Contains(events.Skip(secondDeathIdx), e => e is HeroStatsSyncEvent { Side: BattleSide.A });
    }

    [Fact]
    public void JieXianBreakthroughRaisesMaxMpAndSyncs()
    {
        var session = BattleTestHelper.CreateSession(rosterA: new[] { 2 }, rosterB: new[] { 4 });
        BattleTestHelper.EnterActionPhase(session, 1);
        int maxMpBefore = session.PlayerA.Current!.Stats.MaxMp;

        session.Execute(new SubmitActionCommand(BattleSide.A, new CastSkillAction(Data.SkillSlot.Q)));
        session.Execute(new SubmitActionCommand(BattleSide.B, new SkipAction()));
        BattleTestHelper.Tick(session, 5.01);

        Assert.Equal(maxMpBefore + 1, session.PlayerA.Current.Stats.MaxMp);

        var sync = session.Log.OfType<HeroStatsSyncEvent>().Last(e => e.Side == BattleSide.A);
        Assert.Equal(maxMpBefore + 1, sync.MaxMp);
    }
}
