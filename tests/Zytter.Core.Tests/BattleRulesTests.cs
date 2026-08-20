using Zytter.Core.Battle;
using Zytter.Core.Common;
using Zytter.Core.Rules;

namespace Zytter.Core.Tests;

public class DamageFormulaTests
{
    private static BattleSession Session(ulong seed = 42) => BattleTestHelper.CreateSession(seed);

    [Fact]
    public void BasicAttackUsesLinearArmorReduction()
    {
        // 奕阳 atk6 vs 罗天杰 def1：d = 6 - round(1*(1-0)) = 5
        var session = Session();
        var attacker = session.PlayerA.Current!;
        var defender = session.PlayerB.Current!;
        int hpBefore = defender.Stats.Hp;

        var result = DamageCalculator.BasicAttack(session, attacker, defender);

        Assert.False(result.Dodged);
        Assert.Equal(5, result.FinalDamage);
        Assert.Equal(hpBefore - 5, defender.Stats.Hp);
    }

    [Fact]
    public void BasicAttackDamageNeverNegative()
    {
        // 张可汐 atk3 vs 张枫 def3：d = 3 - 3 = 0
        var session = BattleTestHelper.CreateSession(seed: 7, rosterA: new[] { 8 }, rosterB: new[] { 5 });
        var attacker = session.PlayerA.Current!;
        var defender = session.PlayerB.Current!;

        var result = DamageCalculator.BasicAttack(session, attacker, defender);
        Assert.Equal(0, result.FinalDamage);
    }

    [Fact]
    public void ArmorPenetrationReducesEffectiveArmor()
    {
        // 长剑 30% 物穿：d = 6 - round(1 * 0.7) = 6
        var session = Session();
        var attacker = session.PlayerA.Current!;
        var defender = session.PlayerB.Current!;
        attacker.Equipment.Consumed.Add(15); // 长剑-朝醉青烟 atk+5 adp+0.3

        var result = DamageCalculator.BasicAttack(session, attacker, defender);
        // atk 6+5=11, def 1*(1-0.3)=0.7→1, d = 11-1 = 10
        Assert.Equal(10, result.FinalDamage);
    }

    [Fact]
    public void MagicDamageUsesMagicDefenseReduction()
    {
        // 郈与却 W 强力剥削目标魔抗：先入为主 +30% 魔穿后 7 - round(3*(0.7)) = 7-2 = 5
        var session = Session();
        var attacker = session.PlayerA.Current!;   // 奕阳 app 0
        var defender = session.PlayerB.Current!;   // 罗天杰 adf 3

        int d = DamageCalculator.ComputeMagicDamage(session, attacker, defender, 7);
        Assert.Equal(4, d); // 7 - round(3*1) = 4
    }

    [Fact]
    public void MagicPenetrationFromBuffApplies()
    {
        var session = Session();
        var attacker = session.PlayerA.Current!;
        var defender = session.PlayerB.Current!;

        var def = session.Catalog.GetBuff("first_move");
        session.ApplyBuff(attacker, def, 1, 3);

        // app 0.3 → 7 - round(3*0.7) = 7-2 = 5
        int d = DamageCalculator.ComputeMagicDamage(session, attacker, defender, 7);
        Assert.Equal(5, d);
    }

    [Fact]
    public void TrueDamageIgnoresEverything()
    {
        var session = Session();
        var defender = session.PlayerB.Current!;
        int hpBefore = defender.Stats.Hp;

        DamageCalculator.True(session, defender, 9);
        Assert.Equal(hpBefore - 9, defender.Stats.Hp);
    }

    [Fact]
    public void IceWingsAbsorbPhysicalDamageAndBreakAt20()
    {
        var session = Session();
        var defender = session.PlayerB.Current!; // 罗天杰
        session.ApplyBuff(defender, session.Catalog.GetBuff("ice_wings"), 1, 3);

        int hpBefore = defender.Stats.Hp;
        // 奕阳 atk6 vs def1 → 每击 5 点，全被羽翼吸收
        for (int i = 0; i < 4; i++)
            DamageCalculator.BasicAttack(session, session.PlayerA.Current!, defender);

        Assert.Equal(hpBefore, defender.Stats.Hp); // 无实际伤害
        Assert.False(defender.Buffs.Has("ice_wings")); // 累计 20 → 破碎
    }

    [Fact]
    public void HourglassAbsorbsAllDamage()
    {
        var session = Session();
        var defender = session.PlayerB.Current!;
        defender.State["hourglass_rounds"] = 4;
        session.ApplyBuff(defender, session.Catalog.GetBuff("hourglass"), 1, -1);

        int hpBefore = defender.Stats.Hp;
        DamageCalculator.BasicAttack(session, session.PlayerA.Current!, defender);
        Assert.Equal(hpBefore, defender.Stats.Hp);
        Assert.Equal(5, session.PlayerB.Current!.Buffs.Get("hourglass")!.V1);
    }

    [Fact]
    public void GuardsBlockUpToFourPerHit()
    {
        // 苏璟静有 R 技能（公主号令）
        var session = BattleTestHelper.CreateSession(rosterA: new[] { 1 }, rosterB: new[] { 11 });
        var defender = session.PlayerB.Current!;
        defender.GetSkill(Data.SkillSlot.R)!.SetState("guards", 1);
        session.ApplyBuff(defender, session.Catalog.GetBuff("princess_order"), 1, -1);

        int hpBefore = defender.Stats.Hp;
        DamageCalculator.BasicAttack(session, session.PlayerA.Current!, defender);
        // 奕阳 atk6 vs 苏璟静 def2 → 4 点全被禁卫军抵挡
        Assert.Equal(hpBefore, defender.Stats.Hp);
        Assert.Equal(0, defender.GetSkill(Data.SkillSlot.R)!.GetState("guards"));
    }
}

public class VictoryJudgeTests
{
    [Fact]
    public void RoundExhaustionPrefersMoreRemainingHeroes()
    {
        var session = BattleTestHelper.CreateSession();
        // B 方已死 1 名英雄（换人后 RosterIndex=1）
        session.PlayerB.RosterIndex = 1;

        var winner = VictoryJudge.JudgeByRoundExhaustion(session.PlayerA, session.PlayerB);
        Assert.Equal(BattleSide.A, winner);
    }

    [Fact]
    public void RoundExhaustionPrefersHigherHpPercent()
    {
        var session = BattleTestHelper.CreateSession();
        session.PlayerA.Current!.Stats.AddHp(-10); // 31→21, 67.7%
        session.PlayerB.Current!.Stats.AddHp(-15); // 33→18, 54.5%

        var winner = VictoryJudge.JudgeByRoundExhaustion(session.PlayerA, session.PlayerB);
        Assert.Equal(BattleSide.A, winner);
    }

    [Fact]
    public void RoundExhaustionTieGoesToRoomOwner()
    {
        // 同英雄同血量 → 数量/百分比/具体值全平 → 房主胜
        var session = BattleTestHelper.CreateSession(rosterA: new[] { 1 }, rosterB: new[] { 1 });
        var winner = VictoryJudge.JudgeByRoundExhaustion(session.PlayerA, session.PlayerB);
        Assert.Equal(BattleSide.A, winner);
    }

    [Fact]
    public void HpPercentComparisonUsesFloatingPoint()
    {
        // 修复原版整数除法缺陷：10/20 (50%) vs 1/3 (33%) 必须正确比较
        var session = BattleTestHelper.CreateSession();
        session.PlayerA.Current!.Stats.AddHp(-21); // 31→10
        session.PlayerB.Current!.Stats.AddHp(-32); // 33→1

        var winner = VictoryJudge.JudgeByRoundExhaustion(session.PlayerA, session.PlayerB);
        Assert.Equal(BattleSide.A, winner); // 10/31 > 1/33
    }
}

public class SkillBehaviorTests
{
    [Fact]
    public void BurningAppliesStacksAndTicksDamage()
    {
        var session = BattleTestHelper.CreateSession(seed: 1, rosterA: new[] { 1 }, rosterB: new[] { 4 });
        BattleTestHelper.EnterActionPhase(session, 1);

        var yy = session.PlayerA.Current!;
        var target = session.PlayerB.Current!;
        // 结晶2 100% 命中：直接施加灼烧
        yy.CrystalActive = true;
        yy.CrystalBranch = 2;

        session.Execute(new SubmitActionCommand(BattleSide.A, new CastSkillAction(Data.SkillSlot.Q)));
        session.Execute(new SubmitActionCommand(BattleSide.B, new SkipAction()));
        BattleTestHelper.Tick(session, 5.01);

        // 原版：灼烧在施放当回合的结算末就 tick 一次（3 → 2）
        Assert.Equal(2, target.State.GetValueOrDefault("burn_stacks"));
        Assert.True(target.Buffs.Has("burn"));
    }

    [Fact]
    public void BoneBreakerSelfDamageAndTargetFloor()
    {
        var session = BattleTestHelper.CreateSession(rosterA: new[] { 4 }, rosterB: new[] { 1 });
        var ltj = session.PlayerA.Current!;
        var target = session.PlayerB.Current!;

        // 上场 ≥2 回合
        ltj.HeroTime = 2;
        // 把目标打到 1 血
        target.Stats.AddHp(-(target.Stats.Hp - 1));

        var effect = session.SkillEffects.Get("ltj_e");
        var ctx = new SkillCastContext
        {
            Session = session,
            Caster = ltj,
            Target = target,
            Slot = Data.SkillSlot.E,
            Runtime = ltj.GetSkill(Data.SkillSlot.E)!,
        };
        effect.Validate(ctx);
        ltj.Stats.AddMp(-ctx.Runtime.Definition.Mp);
        effect.Execute(ctx);

        Assert.Equal(ltj.Stats.Hp, ltj.Stats.MaxHp - 7); // 自损 7
        Assert.Equal(2, target.Stats.Hp); // 生命保底 2
    }

    [Fact]
    public void MeteorRespectsOddRoundOnly()
    {
        var session = BattleTestHelper.CreateSession(rosterA: new[] { 9 }, rosterB: new[] { 4 });
        BattleTestHelper.EnterActionPhase(session, 2); // 第 2 回合（双数）

        var zxy = session.PlayerA.Current!;
        var effect = session.SkillEffects.Get("zxy_w");
        var ctx = new SkillCastContext
        {
            Session = session,
            Caster = zxy,
            Target = session.PlayerB.Current,
            Slot = Data.SkillSlot.W,
            Runtime = zxy.GetSkill(Data.SkillSlot.W)!,
        };
        Assert.Throws<RuleViolationException>(() => effect.Validate(ctx));
    }

    [Fact]
    public void PraiseBurnsEnemyMpOnNextMagicDamage()
    {
        var session = BattleTestHelper.CreateSession(rosterA: new[] { 9 }, rosterB: new[] { 4 });
        var zxy = session.PlayerA.Current!;
        var target = session.PlayerB.Current!;

        zxy.GetSkill(Data.SkillSlot.Q)!.SetState("praise_active", 1);
        session.ApplyBuff(zxy, session.Catalog.GetBuff("praise"), 1, -1);

        int mpBefore = target.Stats.Mp;
        DamageCalculator.Magic(session, zxy, target, 10, false);

        int expectedBurn = (int)Math.Round(target.Stats.MaxMp * 0.42);
        Assert.Equal(Math.Max(0, mpBefore - expectedBurn), target.Stats.Mp);
        Assert.Equal(0, zxy.GetSkill(Data.SkillSlot.Q)!.GetState("praise_active"));
    }

    [Fact]
    public void LiberationStacksGiveAttackAndDefense()
    {
        var session = BattleTestHelper.CreateSession(rosterA: new[] { 2 }, rosterB: new[] { 4 });
        var lxs = session.PlayerA.Current!;

        var effect = session.SkillEffects.Get("lxs_w");
        var ctx = new SkillCastContext
        {
            Session = session,
            Caster = lxs,
            Target = null,
            Slot = Data.SkillSlot.W,
            Runtime = lxs.GetSkill(Data.SkillSlot.W)!,
        };
        lxs.Stats.AddMp(-ctx.Runtime.Definition.Mp);
        effect.Execute(ctx);
        effect.Execute(ctx);

        Assert.Equal(2, lxs.Buffs.Get("liberation")!.Stacks);
        Assert.Equal(10, StatsResolver.Attack(session, lxs));  // 6 + 2*2
        Assert.Equal(5, StatsResolver.Defense(session, lxs));  // 3 + 1*2
    }

    [Fact]
    public void DemonicRageKillsInstantlyAndRefillsMp()
    {
        var session = BattleTestHelper.CreateSession(seed: 1, rosterA: new[] { 2 }, rosterB: new[] { 4 });
        var lxs = session.PlayerA.Current!;
        var target = session.PlayerB.Current!;

        // 强制成功：kill_chance = 10 → 必杀
        lxs.GetSkill(Data.SkillSlot.E)!.SetState("kill_chance", 10);
        lxs.Stats.AddMp(-3); // 消耗部分魔法

        var effect = session.SkillEffects.Get("lxs_e");
        var ctx = new SkillCastContext
        {
            Session = session,
            Caster = lxs,
            Target = target,
            Slot = Data.SkillSlot.E,
            Runtime = lxs.GetSkill(Data.SkillSlot.E)!,
        };
        effect.Execute(ctx);

        Assert.True(target.IsDead);
        Assert.Equal(lxs.Stats.MaxMp, lxs.Stats.Mp); // 回满蓝
        Assert.Equal(8, lxs.GetSkill(Data.SkillSlot.E)!.GetState("kill_chance")); // 30%→(成功)-20%... 10-2
    }

    [Fact]
    public void OracleViolationLosesTwoDefensePermanently()
    {
        var session = BattleTestHelper.CreateSession(rosterA: new[] { 12 }, rosterB: new[] { 4 });
        BattleTestHelper.EnterActionPhase(session, 1);

        var w = session.PlayerA.Current!;
        var target = session.PlayerB.Current!;
        target.State["oracle_rule"] = 1; // 必须普攻
        session.ApplyBuff(target, session.Catalog.GetBuff("oracle"), 1, -1);

        int defBefore = target.Stats.Defense;
        session.Execute(new SubmitActionCommand(BattleSide.B, new CastSkillAction(Data.SkillSlot.W)));
        Assert.Equal(defBefore - 2, target.Stats.Defense);
    }

    [Fact]
    public void StarRushStealsArmorNextNextRound()
    {
        var session = BattleTestHelper.CreateSession(rosterA: new[] { 3 }, rosterB: new[] { 4 });
        var ysn = session.PlayerA.Current!;
        var target = session.PlayerB.Current!;
        int targetDefBefore = target.Stats.Defense;
        int casterDefBefore = ysn.Stats.Defense;

        var effect = session.SkillEffects.Get("ysn_q");
        var ctx = new SkillCastContext
        {
            Session = session,
            Caster = ysn,
            Target = target,
            Slot = Data.SkillSlot.Q,
            Runtime = ysn.GetSkill(Data.SkillSlot.Q)!,
        };
        ysn.Stats.AddMp(-ctx.Runtime.Definition.Mp);
        effect.Execute(ctx);

        // 偷甲在 r+2 生效：推进 2 个回合
        BattleTestHelper.Tick(session, 5.01);
        BattleTestHelper.EnterActionPhase(session, 3);
        BattleTestHelper.Tick(session, 0.01);

        // 偷取 min(护甲,3)=1（罗天杰 def1）
        Assert.Equal(targetDefBefore - 1, target.Stats.Defense);
        Assert.Equal(casterDefBefore + 1, ysn.Stats.Defense);
    }

    [Fact]
    public void YiYangSkillsEmitLuckRollEvents()
    {
        var session = BattleTestHelper.CreateSession(seed: 5, rosterA: new[] { 1 }, rosterB: new[] { 4 });
        BattleTestHelper.EnterActionPhase(session, 1);

        // 释放暗影之刺（W：幸运数字判定）
        session.Execute(new SubmitActionCommand(BattleSide.A, new CastSkillAction(Data.SkillSlot.W)));
        session.Execute(new SubmitActionCommand(BattleSide.B, new SkipAction()));
        BattleTestHelper.Tick(session, 5.01);

        Assert.Contains(session.Log, e => e is LuckRollEvent { SkillName: "暗影之刺" });
        var roll = session.Log.OfType<LuckRollEvent>().First(e => e.SkillName == "暗影之刺");
        Assert.InRange(roll.Rolled, 0, 9);
    }

    [Fact]
    public void OracleViolationEmitsResultInfo()
    {
        var session = BattleTestHelper.CreateSession(rosterA: new[] { 12 }, rosterB: new[] { 4 });
        BattleTestHelper.EnterActionPhase(session, 1);

        var target = session.PlayerB.Current!;
        target.State["oracle_rule"] = 1; // 必须普攻
        session.ApplyBuff(target, session.Catalog.GetBuff("oracle"), 1, -1);

        session.Execute(new SubmitActionCommand(BattleSide.B, new CastSkillAction(Data.SkillSlot.W)));

        Assert.Contains(session.Log, e => e is SkillInfoEvent { Key: "oracle_result", Value: 1 });
    }
}
