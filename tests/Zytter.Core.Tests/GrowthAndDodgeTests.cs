using Zytter.Core.Battle;
using Zytter.Core.Buffs;
using Zytter.Core.Rules;

namespace Zytter.Core.Tests;

/// <summary>商店成长补足与闪避判定留痕的回归测试。</summary>
public class ShopGrowthTests
{
    [Fact]
    public void GrowthCountsOncePerShopRoundAndRaisesCapOnly()
    {
        var session = BattleTestHelper.CreateSession(); // A=奕阳(31) B=罗天杰(33)
        BattleTestHelper.EnterActionPhase(session, 5);
        BattleTestHelper.PlayRound(session, new SkipAction(), new SkipAction());

        Assert.Equal(BattlePhase.Shop, session.Phase);
        // 第 6 回合商店成长只计数一次（此前双方循环各 +1 导致 heroup 翻倍）
        Assert.Equal(1, session.GrowthLevel);
        // 双方当前英雄：上限 +4、当前血量不变（成长只动上限）
        Assert.Equal(31 + 4, session.PlayerA.Current!.Stats.MaxHp);
        Assert.Equal(31, session.PlayerA.Current!.Stats.Hp);
        Assert.Equal(33 + 4, session.PlayerB.Current!.Stats.MaxHp);
        Assert.Equal(33, session.PlayerB.Current!.Stats.Hp);
    }

    [Fact]
    public void NewHeroAfterGrowthEntersFullWithCompensatedHp()
    {
        // A=杨圣诺(atk7) 持续普攻；B=谢悠涵(hp26/def1) 放弃行动 → 5 回合内阵亡换人
        var session = BattleTestHelper.CreateSession(rosterA: new[] { 3 }, rosterB: new[] { 7, 8 });
        BattleTestHelper.EnterActionPhase(session, 5);
        BattleTestHelper.PlayRound(session, new SkipAction(), new SkipAction());
        Assert.Equal(BattlePhase.Shop, session.Phase);
        Assert.Equal(1, session.GrowthLevel);

        BattleTestHelper.Tick(session, 20); // 关闭商店 → 准备 → 行动
        for (int r = 7; r <= 12 && !session.IsFinished; r++)
        {
            BattleTestHelper.EnterActionPhase(session, r);
            if (session.Phase != BattlePhase.Action) break;
            BattleTestHelper.PlayRound(session, new BasicAttackAction(), new SkipAction());
            if (session.Log.OfType<HeroSwitchedEvent>().Any()) break;
        }

        // 换人瞬间：切换事件 + 紧随其后的权威同步（在后续商店成长干扰数值之前）
        var switched = session.Log.OfType<HeroSwitchedEvent>().Single();
        var sync = session.Log.OfType<HeroStatsSyncEvent>()
            .Where(e => e.Side == BattleSide.B && e.Seq > switched.Seq)
            .OrderBy(e => e.Seq)
            .First();

        Assert.Equal("张可汐", switched.HeroName);
        Assert.Equal(31, switched.MaxHp);        // 27 + heroup=1 的 4 点补足
        Assert.Equal(31, sync.MaxHp);
        Assert.Equal(sync.MaxHp, sync.Hp);       // 满血上场（旧版只加上限不加血 → 换人残血）
        Assert.InRange(session.PlayerB.Current!.Stats.PhysicalDamageReduction, 0.05, 0.10);
    }
}

public class DodgeRollTests
{
    [Fact]
    public void DodgeRollWithFlashEmitsRollAndThreshold()
    {
        var session = BattleTestHelper.CreateSession();
        var attacker = session.PlayerA.Current!;
        var defender = session.PlayerB.Current!;
        session.ApplyBuff(defender, session.Catalog.GetBuff("flash"), 1, 3, BuffApplyMode.Keep);

        int before = session.Log.Count;
        var result = DamageCalculator.BasicAttack(session, attacker, defender);
        var evt = session.Log.Skip(before).OfType<BasicAttackEvent>().Single();

        Assert.Equal(6, evt.DodgeThreshold); // 闪现 60% → 掷出需小于 6
        Assert.InRange(evt.DodgeRoll, 0, 9);
        Assert.Equal(evt.DodgeRoll < 6, evt.Dodged);
        Assert.Equal(evt.Dodged, result.Dodged);
    }

    [Fact]
    public void DodgeRollWithoutFlashHasZeroThresholdAndNeverDodges()
    {
        var session = BattleTestHelper.CreateSession();
        int before = session.Log.Count;
        var result = DamageCalculator.BasicAttack(session, session.PlayerA.Current!, session.PlayerB.Current!);
        var evt = session.Log.Skip(before).OfType<BasicAttackEvent>().Single();

        Assert.Equal(0, evt.DodgeThreshold);
        Assert.InRange(evt.DodgeRoll, 0, 9); // 判定仍然掷骰并留痕
        Assert.False(evt.Dodged);
        Assert.False(result.Dodged);
    }

    [Fact]
    public void FlashPlusTakesEightyPercentThreshold()
    {
        var session = BattleTestHelper.CreateSession();
        var attacker = session.PlayerA.Current!;
        var defender = session.PlayerB.Current!;
        session.ApplyBuff(defender, session.Catalog.GetBuff("flash_plus"), 1, 2, BuffApplyMode.Keep);

        int before = session.Log.Count;
        var result = DamageCalculator.BasicAttack(session, attacker, defender);
        var evt = session.Log.Skip(before).OfType<BasicAttackEvent>().Single();

        Assert.Equal(8, evt.DodgeThreshold);
        Assert.Equal(evt.DodgeRoll < 8, evt.Dodged);
        Assert.Equal(evt.Dodged, result.Dodged);
    }
}
