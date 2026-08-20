using Zytter.Core.Battle;
using Zytter.Core.Buffs;
using Zytter.Core.Common;
using Zytter.Core.Data;
using Zytter.Core.Heroes;
using Zytter.Core.Rules;

namespace Zytter.Core.Skills;

// ==================== 奕阳（英雄 1） ====================

/// <summary>烈日之箭（Q）：幸运数字 ≤ 对方生命个位数 → 施加灼烧 3 回合（可叠加）。结晶2：100% 命中。</summary>
public sealed class YiYangQEffect : ISkillEffect
{
    public int GetPriorityTier(SkillCastContext ctx) => 0;

    public void Validate(SkillCastContext ctx)
    {
        if (ctx.Target is null || ctx.Target.IsDead)
            throw new RuleViolationException("no_target");
    }

    public void Execute(SkillCastContext ctx)
    {
        var session = ctx.Session;
        var target = ctx.Target!;
        bool alwaysHit = ctx.Caster.CrystalActive && ctx.Caster.CrystalBranch == 2;

        if (!alwaysHit && !SkillHelpers.LuckyRoll(session, ctx.Caster, "烈日之箭", target.Stats.Hp % 10))
            return;

        double stacks = target.State.GetValueOrDefault("burn_stacks");
        target.State["burn_stacks"] = stacks + 3;
        session.ApplyBuff(target, session.Catalog.GetBuff("burn"), 3, -1, BuffApplyMode.Keep);
    }
}

/// <summary>暗影之刺（W）：幸运数字 ≤ 行动力个位数 → 造成 (10+屠杀之风加成) 魔法伤害。</summary>
public sealed class YiYangWEffect : ISkillEffect
{
    public int GetPriorityTier(SkillCastContext ctx) => 0;

    public void Validate(SkillCastContext ctx)
    {
        if (ctx.Target is null || ctx.Target.IsDead)
            throw new RuleViolationException("no_target");
    }

    public void Execute(SkillCastContext ctx)
    {
        var session = ctx.Session;
        var caster = ctx.Caster;
        var target = ctx.Target!;

        double xdl = StatsResolver.ActionPower(session, caster);
        if (!SkillHelpers.LuckyRoll(session, caster, "暗影之刺", SkillHelpers.ActionPowerDigit(xdl)))
            return;

        double bonus = caster.GetSkill(SkillSlot.E)?.GetState("magic_bonus") ?? 0;
        SkillHelpers.DealMagic(session, caster, target, (int)(10 + bonus));
    }
}

/// <summary>屠杀之风（E）：幸运数字 ≤ 自身生命个位数 → 魔法伤害加成 +3（结晶3：+6）、行动力 +2、持续 4 回合（可叠加）。</summary>
public sealed class YiYangEEffect : ISkillEffect
{
    public int GetPriorityTier(SkillCastContext ctx) => 0;

    public void Validate(SkillCastContext ctx)
    {
    }

    public void Execute(SkillCastContext ctx)
    {
        var session = ctx.Session;
        var caster = ctx.Caster;

        if (!SkillHelpers.LuckyRoll(session, caster, "屠杀之风", caster.Stats.Hp % 10))
            return;

        double bonus = ctx.Caster.CrystalActive && ctx.Caster.CrystalBranch == 3 ? 6 : 3;
        var e = caster.GetSkill(SkillSlot.E)!;
        e.AddState("magic_bonus", bonus);

        session.ApplyBuff(caster, session.Catalog.GetBuff("slaughter_wind"), 1, 4, BuffApplyMode.Extend);
    }
}

// ==================== 刘晓释（英雄 2） ====================

/// <summary>界限突破（Q）：永久 +1 魔法上限、+1 魔法回复、立即 +1 魔法；W 耗蓝 +1。</summary>
public sealed class LiuXiaoShiQEffect : ISkillEffect
{
    public int GetPriorityTier(SkillCastContext ctx) => 0;

    public void Validate(SkillCastContext ctx)
    {
    }

    public void Execute(SkillCastContext ctx)
    {
        var caster = ctx.Caster;
        caster.Stats.AddMaxMp(1);
        caster.Stats.AddMpRegen(1);
        caster.Stats.AddMp(1);

        var w = caster.GetSkill(SkillSlot.W);
        if (w is not null)
            w.Definition = w.Definition with { Mp = w.Definition.Mp + 1 };
    }
}

/// <summary>解放真名（W）：每层 +2 攻击、+1 护甲，各层独立 10 回合后扣回。</summary>
public sealed class LiuXiaoShiWEffect : ISkillEffect
{
    public int GetPriorityTier(SkillCastContext ctx) => 0;

    public void Validate(SkillCastContext ctx)
    {
    }

    public void Execute(SkillCastContext ctx)
    {
        var session = ctx.Session;
        var caster = ctx.Caster;
        var buff = session.ApplyBuff(caster, session.Catalog.GetBuff("liberation"), 1, -1, BuffApplyMode.Keep);
        buff.StackExpiryRends.Add(session.Rend + 11); // 持续 10 回合：rend 到达 r+10 时扣回
    }
}

/// <summary>魔王怒（E）：30% 概率（动态成长）秒杀对方（无视一切免疫），成功回满蓝、概率-20%；失败概率+10%。</summary>
public sealed class LiuXiaoShiEEffect : ISkillEffect
{
    public const double InitialChance = 3; // 3/10 = 30%

    public int GetPriorityTier(SkillCastContext ctx) => 0;

    public void Validate(SkillCastContext ctx)
    {
        if (ctx.Target is null || ctx.Target.IsDead)
            throw new RuleViolationException("no_target");
    }

    public void Execute(SkillCastContext ctx)
    {
        var session = ctx.Session;
        var caster = ctx.Caster;
        var target = ctx.Target!;
        var e = caster.GetSkill(SkillSlot.E)!;

        double chance = e.GetState("kill_chance", InitialChance);
        bool killed = SkillHelpers.LuckyRoll(session, caster, "魔王怒", (int)chance);

        if (killed)
        {
            // 秒杀：无视时光沙漏等一切免疫；累计伤害统计但不走伤害计算链
            int hp = target.Stats.Hp;
            target.Stats.AddHp(-hp);
            caster.DamageDealt += hp;
            session.Emit(new DamageDealtEvent(session.NextSeq(), target.Side, target.Id, hp, DamageType.True));

            caster.Stats.AddMp(caster.Stats.MaxMp);
            e.SetState("kill_chance", Math.Max(0, chance - 2));
        }
        else
        {
            e.SetState("kill_chance", Math.Min(10, chance + 1));
        }
        session.EmitSkillInfo(caster.Side, "kill_chance", (int)e.GetState("kill_chance"));
    }
}

// ==================== 杨圣诺（英雄 3） ====================

/// <summary>新星冲刺（Q）：7 点魔法伤害；下回合偷取至多 3 点护甲 1 回合。</summary>
public sealed class YangShengNuoQEffect : ISkillEffect
{
    public int GetPriorityTier(SkillCastContext ctx) => 0;

    public void Validate(SkillCastContext ctx)
    {
        if (ctx.Target is null || ctx.Target.IsDead)
            throw new RuleViolationException("no_target");
    }

    public void Execute(SkillCastContext ctx)
    {
        var session = ctx.Session;
        SkillHelpers.DealMagic(session, ctx.Caster, ctx.Target!, 7);
        session.ScheduleStarRush(ctx.Caster.Side);
    }
}

/// <summary>星辰陨落（W）：敌方魔抗 -2 持续 2 回合（可叠加）；可选追加 Q（由 ChainQ 处理）。</summary>
public sealed class YangShengNuoWEffect : ISkillEffect
{
    public int GetPriorityTier(SkillCastContext ctx) => 0;

    public void Validate(SkillCastContext ctx)
    {
        if (ctx.Target is null || ctx.Target.IsDead)
            throw new RuleViolationException("no_target");
    }

    public void Execute(SkillCastContext ctx)
    {
        var session = ctx.Session;
        session.ApplyBuff(ctx.Target!, session.Catalog.GetBuff("star_fall"), 1, 2, BuffApplyMode.Extend);
    }
}

// ==================== 张枫（英雄 5） ====================

/// <summary>一秒十三刀（Q）：造成攻击力等值的魔法伤害。</summary>
public sealed class ZhangFengQEffect : ISkillEffect
{
    public int GetPriorityTier(SkillCastContext ctx) => 0;

    public void Validate(SkillCastContext ctx)
    {
        if (ctx.Target is null || ctx.Target.IsDead)
            throw new RuleViolationException("no_target");
    }

    public void Execute(SkillCastContext ctx)
    {
        var session = ctx.Session;
        int baseDamage = (int)Math.Round(StatsResolver.Attack(session, ctx.Caster));
        SkillHelpers.DealMagic(session, ctx.Caster, ctx.Target!, baseDamage);
    }
}

/// <summary>风之结界（W）：敌方下一回合完全行动不能；70% 追加下一回合行动受限。挂起期间不可重复施放。</summary>
public sealed class ZhangFengWEffect : ISkillEffect
{
    public int GetPriorityTier(SkillCastContext ctx) => 0;

    public void Validate(SkillCastContext ctx)
    {
        if (ctx.Target is null || ctx.Target.IsDead)
            throw new RuleViolationException("no_target");
        var w = ctx.Caster.GetSkill(SkillSlot.W)!;
        if (w.GetState("pending") > 0)
            throw new RuleViolationException("wind_barrier_pending");
    }

    public void Execute(SkillCastContext ctx)
    {
        var session = ctx.Session;
        var target = ctx.Target!;

        if (session.TryApplyControl(target, CombatStatus.Incapacitated, "风之结界"))
            session.ApplyBuff(target, session.Catalog.GetBuff("wind_barrier_stun"), 1, -1, BuffApplyMode.Keep);

        // 70% 追加行动受限（完全行动不能后的下一个回合）
        if (SkillHelpers.LuckyRoll(session, ctx.Caster, "风之结界", 6))
        {
            ctx.Caster.GetSkill(SkillSlot.W)!.SetState("pending", 1);
            session.ScheduleWindBarrier(target.Side);
        }
    }
}

/// <summary>审判之斩（E）：造成对方已损失生命值 80% 的魔法伤害。</summary>
public sealed class ZhangFengEEffect : ISkillEffect
{
    public int GetPriorityTier(SkillCastContext ctx) => 0;

    public void Validate(SkillCastContext ctx)
    {
        if (ctx.Target is null || ctx.Target.IsDead)
            throw new RuleViolationException("no_target");
    }

    public void Execute(SkillCastContext ctx)
    {
        var session = ctx.Session;
        var target = ctx.Target!;
        int lost = target.Stats.MaxHp - target.Stats.Hp;
        int baseDamage = (int)Math.Round(0.8 * lost);
        SkillHelpers.DealMagic(session, ctx.Caster, target, baseDamage);
    }
}
