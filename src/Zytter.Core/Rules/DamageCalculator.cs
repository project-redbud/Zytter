using Zytter.Core.Battle;
using Zytter.Core.Buffs;
using Zytter.Core.Data;

namespace Zytter.Core.Rules;

/// <summary>一次伤害结算的结果（供事件广播与统计）。</summary>
public sealed class DamageResult
{
    /// <summary>结算前原始伤害。</summary>
    public int RawDamage { get; init; }

    /// <summary>实际扣血（经护甲/减伤/禁卫军/免疫后）。</summary>
    public int FinalDamage { get; init; }

    public bool Dodged { get; init; }

    /// <summary>被冰之羽翼完全免疫（累计进羽翼）。</summary>
    public bool AbsorbedByWings { get; init; }

    /// <summary>被时光沙漏吸收。</summary>
    public bool AbsorbedByHourglass { get; init; }

    public bool IsLethal => FinalDamage > 0;
}

/// <summary>
/// 伤害结算器。三条链（普通攻击/物理技能/魔法）逐行对照旧版
/// Fight.java 的 balanceatk / balancephyical / balanceskill / balancezy / balancereal 实现，
/// 保留原版全部行为细节（包括"时光沙漏吸收后仍享受结晶加成"这类顺序怪癖），
/// 修复了旧版崩溃类缺陷（心源神域 mp=0 抛异常、整数除法误判）。
/// </summary>
public static class DamageCalculator
{
    private const string PurityKey = "purity";
    private const string GuardsKey = "guards";
    private const string BurnKey = "burn_stacks";
    private const string MagicBonusKey = "magic_bonus";

    // ==================== 普通攻击（balanceatk） ====================

    public static DamageResult BasicAttack(BattleSession session, Combatant attacker, Combatant defender, bool swordDanceHalf = false)
    {
        // 1. 闪避判定：掷 p∈[0,9]；闪现 60%（p<6）、闪现+ 80%（p<8），无闪避能力时不触发
        int p = session.Rng.Next(10);
        int dodgeThreshold = defender.Buffs.Has("flash_plus") ? 8 : defender.Buffs.Has("flash") ? 6 : 0;
        bool dodged = dodgeThreshold > 0 && p < dodgeThreshold;

        if (dodged)
        {
            session.Emit(new BasicAttackEvent(session.NextSeq(), attacker.Side, Dodged: true, p, dodgeThreshold));
            return new DamageResult { RawDamage = 0, FinalDamage = 0, Dodged = true };
        }

        double attack = StatsResolver.Attack(session, attacker);
        double defense = StatsResolver.Defense(session, defender);
        double armorPen = StatsResolver.ArmorPenetration(session, attacker);

        int d = Math.Max(0, (int)Math.Round(attack - Math.Round(defense * (1 - armorPen))));

        // 鹰角弓：普通攻击 ×1.42
        if (attacker.Equipment.IsWorn(16))
            d = (int)Math.Round(d * 1.42);

        session.Emit(new BasicAttackEvent(session.NextSeq(), attacker.Side, Dodged: false, p, dodgeThreshold));

        var result = PhysicalCommon(session, attacker, defender, d, swordDanceHalf, DamageSourceKind.BasicAttack, skipLoveFlowerInWings: true);

        // 光炽剑（苏璟静E）：下一次普攻命中（造成伤害）→ 回血 + 附加魔法伤害
        if (!result.Dodged && result.FinalDamage > 0 && attacker.Buffs.Has("light_sword"))
        {
            bool crystal1 = attacker.CrystalActive && attacker.CrystalBranch == 1;
            int heal = crystal1 ? 5 : 3;
            int actualHeal = attacker.Stats.AddHp(heal);
            if (actualHeal > 0)
                session.Emit(new HealedEvent(session.NextSeq(), attacker.Side, attacker.Id, actualHeal));

            int magicBase = crystal1 ? 9 : 6;
            int magicD = ComputeMagicDamage(session, attacker, defender, magicBase);
            Magic(session, attacker, defender, magicD, isEquipmentPassive: false);

            if (session.RemoveBuff(attacker, "light_sword"))
            {
                // 事件已由 RemoveBuff 广播
            }
        }

        return result;
    }

    // ==================== 物理技能（balancephyical） ====================

    public static DamageResult PhysicalSkill(BattleSession session, Combatant attacker, Combatant defender, int rawDamage, DamageSourceKind sourceKind = DamageSourceKind.Skill)
    {
        int d = Math.Max(0, rawDamage);
        return PhysicalCommon(session, attacker, defender, d, swordDanceHalf: false, sourceKind, skipLoveFlowerInWings: false);
    }

    /// <summary>
    /// 物理伤害公共链。原版两条物理链的差异：
    /// - 普攻在冰之羽翼吸收分支中会先结算坚韧之盾/defrate/剑舞半伤但跳过予恋之花（源码怪癖，如实保留）；
    /// - 物理技能在羽翼吸收分支中不做任何减伤（原样累计）。
    /// </summary>
    private static DamageResult PhysicalCommon(
        BattleSession session, Combatant attacker, Combatant defender, int d,
        bool swordDanceHalf, DamageSourceKind sourceKind, bool skipLoveFlowerInWings)
    {
        var wings = defender.Buffs.Get("ice_wings");

        if (wings is not null)
        {
            // 冰之羽翼完全免疫物理伤害，累计至 20 破碎 → 回复 round(剩余回合*0.6) 生命
            if (!skipLoveFlowerInWings)
            {
                // 物理技能：羽翼分支无任何减伤（原样累计）
            }
            else
            {
                // 普攻：羽翼分支先结算坚韧之盾/defrate/剑舞半伤（源码顺序），但跳过予恋之花
                if (defender.Equipment.IsWorn(22))
                    d = (int)Math.Round(d * 0.75);
                d = (int)Math.Round(d * (1 - StatsResolver.PhysicalDamageReduction(session, defender)));
                if (swordDanceHalf)
                    d = (int)Math.Round(d * 0.5);
            }

            wings.V1 += d;
            session.Emit(new BuffAppliedEvent(session.NextSeq(), defender.Side, defender.Id, "ice_wings", "冰之羽翼", wings.Stacks, wings.RemainingRounds));

            if (wings.V1 >= 20)
            {
                int rounds = wings.RemainingRounds < 0 ? 0 : wings.RemainingRounds;
                int heal = (int)Math.Round(rounds * 0.6);
                defender.Buffs.Remove("ice_wings");
                session.Emit(new BuffRemovedEvent(session.NextSeq(), defender.Side, defender.Id, "ice_wings", "冰之羽翼"));
                if (heal > 0)
                {
                    defender.Stats.AddHp(heal);
                    session.Emit(new HealedEvent(session.NextSeq(), defender.Side, defender.Id, heal));
                }
            }

            // 羽翼分支后的装备触发：二阶红月/紫月仍会触发（源码位置在 else 之外）；破军之矛不触发
            AfterPhysicalDamage(session, attacker, defender, dealt: 0, procPojun: false);
            return new DamageResult { RawDamage = d, FinalDamage = 0, AbsorbedByWings = true };
        }

        // 正常链：坚韧者之盾 ×0.75 → 予恋之花 ×0.2（标记回蓝封锁）→ defrate → 剑舞半伤
        if (defender.Equipment.IsWorn(22))
            d = (int)Math.Round(d * 0.75);

        if (defender.Buffs.Has("love_flower_user"))
        {
            defender.MpRegenBlocked = true;
            d = (int)Math.Round(d * 0.2);
        }

        d = (int)Math.Round(d * (1 - StatsResolver.PhysicalDamageReduction(session, defender)));

        if (swordDanceHalf)
            d = (int)Math.Round(d * 0.5);

        int final = ApplyDefenderCommon(session, attacker, defender, d, DamageType.Physical, sourceKind);
        AfterPhysicalDamage(session, attacker, defender, dealt: final, procPojun: true);

        return new DamageResult { RawDamage = d, FinalDamage = final };
    }

    /// <summary>物理伤害后的装备触发：破军之矛重伤、二阶红月、紫月延迟。破军之矛仅在实际造成物理伤害时触发。</summary>
    private static void AfterPhysicalDamage(BattleSession session, Combatant attacker, Combatant defender, int dealt, bool procPojun)
    {
        if (procPojun && dealt > 0 && attacker.Equipment.IsWorn(19) && attacker.State.GetValueOrDefault("pojun_cd") <= 0)
        {
            attacker.State["pojun_cd"] = 2;
            defender.MpRegenBlocked = true;
        }

        if (attacker.Equipment.IsWorn(27))
            session.ProcRedMoon(attacker, defender);

        if (attacker.Equipment.IsWorn(13))
            session.ScheduleZiyue(attacker, defender);
    }

    // ==================== 魔法伤害（balanceskill / balancezy） ====================

    /// <summary>
    /// 魔法伤害（技能/灼烧/装备被动统一入口）。
    /// 原版调用方先按公式算好魔抗削减后的伤害再传入 balanceskill/balancezy，
    /// 此处 finalDamage 即为削减后的值（使用 <see cref="ComputeMagicDamage"/> 计算）。
    /// isEquipmentPassive=true 对应原版 balancezy（禁卫军不抵挡、不触发冰雪十字成长/二阶红月/紫月）。
    /// </summary>
    public static DamageResult Magic(BattleSession session, Combatant attacker, Combatant defender, int finalDamage, bool isEquipmentPassive = false)
    {
        int d = Math.Max(0, finalDamage);

        // 洁静点累计（谢悠涵被动，受任何伤害 +d，上限 8，Q 耗蓝同步）
        AccumulatePurity(session, defender, d);

        if (!isEquipmentPassive)
        {
            // 禁卫军：每名抵挡至多 4 点（一次伤害只牺牲一名）
            d = BlockByGuard(session, defender, d);
        }

        // 时光沙漏吸收
        bool hourglassAbsorbed = false;
        var hourglass = defender.Buffs.Get("hourglass");
        if (hourglass is not null)
        {
            hourglass.V1 += d;
            hourglassAbsorbed = true;
            d = 0;
        }

        // 郈与却结晶3：伤害 +30%——原版在吸收之后才加成（顺序怪癖，如实保留）
        if (attacker.CrystalActive && attacker.CrystalBranch == 3 && attacker.Buffs.Has("first_move"))
        {
            var buff = attacker.Buffs.Get("first_move")!;
            double bonus = buff.V1; // hyqJ 累计值
            d += (int)Math.Round(d * bonus);
        }

        int final = DeductHp(session, attacker, defender, d, DamageType.Magical);

        // 冰雪十字成长：张可汐单次魔法伤害 >7 计数，每满 2 次 Q 耗蓝 +1（上限 4 次，即耗蓝最高 9）
        if (!isEquipmentPassive && d > 7 && attacker.Hero.Id == 8)
        {
            var q = attacker.GetSkill(SkillSlot.Q);
            if (q is not null)
            {
                double count = q.GetState("growth_count");
                count++;
                q.SetState("growth_count", count);
                if (count % 2 == 0 && count < 10)
                    q.Definition = q.Definition with { Mp = q.Definition.Mp + 1 };
            }
        }

        // 礼赞：下一次魔法伤害扣对方 42% 最大魔法值（技能与装备被动伤害均触发）
        if (attacker.Hero.Id == 9)
        {
            var q = attacker.GetSkill(SkillSlot.Q);
            if (q is not null && q.GetState("praise_active") > 0)
            {
                q.SetState("praise_active", 0);
                int mpBurn = (int)Math.Round(defender.Stats.MaxMp * 0.42);
                defender.Stats.AddMp(-mpBurn);
                session.Emit(new MpChangedEvent(session.NextSeq(), defender.Side, defender.Id, -mpBurn));
            }
        }

        // 学生会会徽：魔法伤害的 30% 回血
        if (attacker.Equipment.IsWorn(25) && d > 0)
        {
            int heal = (int)Math.Round(d * 0.3);
            int actual = attacker.Stats.AddHp(heal);
            if (actual > 0)
                session.Emit(new HealedEvent(session.NextSeq(), attacker.Side, attacker.Id, actual));
        }

        // 二阶红月/紫月仅由技能魔法伤害触发（装备被动伤害不递归触发）
        if (!isEquipmentPassive)
        {
            if (attacker.Equipment.IsWorn(27))
                session.ProcRedMoon(attacker, defender);
            if (attacker.Equipment.IsWorn(13))
                session.ScheduleZiyue(attacker, defender);
        }

        return new DamageResult { RawDamage = finalDamage, FinalDamage = final, AbsorbedByHourglass = hourglassAbsorbed };
    }

    /// <summary>魔法伤害基础公式：baseDamage - round((1-魔穿)*魔抗)，下限 0。</summary>
    public static int ComputeMagicDamage(BattleSession session, Combatant attacker, Combatant defender, int baseDamage)
    {
        double magicPen = StatsResolver.MagicPenetration(session, attacker);
        double magicDef = StatsResolver.MagicDefense(session, defender);
        return Math.Max(0, (int)Math.Round(baseDamage - Math.Round((1 - magicPen) * magicDef)));
    }

    // ==================== 真实伤害（balancereal） ====================

    /// <summary>真实伤害：无视一切减伤免疫，直接扣血（洁净之灵双数回合）。</summary>
    public static DamageResult True(BattleSession session, Combatant defender, int damage)
    {
        int final = DeductHp(session, null, defender, damage, DamageType.True);
        return new DamageResult { RawDamage = damage, FinalDamage = final };
    }

    // ==================== 断骨剑（专属物理公式） ====================

    /// <summary>断骨剑：d = round(3*(攻击 - 护甲*(1-物穿)) - 护甲*(1-物穿))，敌方生命保底 2。</summary>
    public static DamageResult BoneBreaker(BattleSession session, Combatant attacker, Combatant defender)
    {
        double attack = StatsResolver.Attack(session, attacker);
        double defense = StatsResolver.Defense(session, defender);
        double armorPen = StatsResolver.ArmorPenetration(session, attacker);
        double reduced = Math.Round(defense * (1 - armorPen));
        int d = Math.Max(0, (int)Math.Round(3 * (attack - reduced) - reduced));

        var result = PhysicalSkill(session, attacker, defender, d, DamageSourceKind.Skill);

        // 敌方生命保底 2（恢复部分以治疗事件同步客户端）
        int hp = defender.Stats.Hp;
        if (hp < 2 && result.FinalDamage > 0)
        {
            int restore = 2 - hp;
            defender.Stats.AddHp(restore);
            session.Emit(new HealedEvent(session.NextSeq(), defender.Side, defender.Id, restore));
        }
        return result;
    }

    // ==================== 公共子链 ====================

    /// <summary>物理伤害公共防御链：洁净点累计 → 禁卫军 → 时光沙漏 → 扣血。</summary>
    private static int ApplyDefenderCommon(BattleSession session, Combatant attacker, Combatant defender, int d, DamageType type, DamageSourceKind sourceKind)
    {
        AccumulatePurity(session, defender, d);
        d = BlockByGuard(session, defender, d);

        var hourglass = defender.Buffs.Get("hourglass");
        if (hourglass is not null)
        {
            hourglass.V1 += d;
            d = 0;
        }

        return DeductHp(session, attacker, defender, d, type);
    }

    /// <summary>洁静点累计（谢悠涵被动：受伤害 +d，上限 8；Q 耗蓝 = 洁净点）。</summary>
    private static void AccumulatePurity(BattleSession session, Combatant defender, int damage)
    {
        if (defender.Hero.Id != 7 || damage <= 0) return;
        var q = defender.GetSkill(SkillSlot.Q);
        if (q is null) return;
        double purity = q.GetState(PurityKey);
        purity = Math.Min(8, purity + damage);
        q.SetState(PurityKey, purity);
        q.Definition = q.Definition with { Mp = (int)purity };
        session.EmitSkillInfo(defender.Side, "purity", (int)purity);
    }

    /// <summary>禁卫军抵挡：一次伤害牺牲一名，抵挡至多 4 点。返回剩余伤害。</summary>
    private static int BlockByGuard(BattleSession session, Combatant defender, int d)
    {
        if (d <= 0) return d;
        var r = defender.GetSkill(SkillSlot.R);
        double guards = r?.GetState(GuardsKey) ?? 0;
        if (guards <= 0) return d;

        int blocked = Math.Min(4, d);
        guards--;
        r!.SetState(GuardsKey, guards);
        if (guards <= 0)
            session.Emit(new BuffRemovedEvent(session.NextSeq(), defender.Side, defender.Id, "princess_order", "公主号令"));
        session.EmitSkillInfo(defender.Side, "guards", (int)guards);
        return d - blocked;
    }

    /// <summary>扣血（含统计与事件）。attacker 为 null 表示无来源（如真实伤害自伤类）。</summary>
    private static int DeductHp(BattleSession session, Combatant? attacker, Combatant defender, int d, DamageType type)
    {
        if (d <= 0) return 0;
        int before = defender.Stats.Hp;
        defender.Stats.AddHp(-d);
        int dealt = before - defender.Stats.Hp;

        if (attacker is not null)
        {
            attacker.DamageDealt += dealt;
            defender.DamageDealt += 0; // 防御方不累计造成伤害
            if (type == DamageType.Physical)
                session.AccumulatePlunder(attacker, defender, dealt);
        }

        session.Emit(new DamageDealtEvent(session.NextSeq(), defender.Side, defender.Id, dealt, type));
        return dealt;
    }
}
