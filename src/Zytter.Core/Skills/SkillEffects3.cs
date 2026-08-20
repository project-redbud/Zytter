using Zytter.Core.Battle;
using Zytter.Core.Buffs;
using Zytter.Core.Common;
using Zytter.Core.Data;
using Zytter.Core.Heroes;
using Zytter.Core.Rules;

namespace Zytter.Core.Skills;

// ==================== 张可汐（英雄 8） ====================

/// <summary>冰雪十字（Q）：(4+成长) 魔法伤害；敌方下一回合完全行动不能。耗蓝随高额魔法伤害成长（上限 9）。</summary>
public sealed class ZhangKeXiQEffect : ISkillEffect
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

        double bonus = caster.GetSkill(SkillSlot.Q)?.GetState("bonus") ?? 0;
        int tideBonus = caster.Buffs.Has("tide_choice_q") ? 4 : 0;
        SkillHelpers.DealMagic(session, caster, target, (int)(4 + bonus + tideBonus));

        if (session.TryApplyControl(target, CombatStatus.Incapacitated, "冰雪十字"))
            session.ApplyBuff(target, session.Catalog.GetBuff("ice_cross"), 1, -1, BuffApplyMode.Keep);
    }
}

/// <summary>冰之羽翼（W）：免疫物理伤害持续 3 回合（回合可叠加），累计 20 点破碎，回复累计伤害 60% 生命。</summary>
public sealed class ZhangKeXiWEffect : ISkillEffect
{
    public int GetPriorityTier(SkillCastContext ctx) => 0;

    public void Validate(SkillCastContext ctx)
    {
    }

    public void Execute(SkillCastContext ctx)
    {
        var session = ctx.Session;
        session.ApplyBuff(ctx.Caster, session.Catalog.GetBuff("ice_wings"), 1, 3, BuffApplyMode.Extend);
    }
}

/// <summary>汐之抉择（E）：永久 Q 与效果二 +2 魔法伤害；随机三选一（Q 加成 3 回合 / 8 点魔法伤害 / 双抗 +4 三回合）。上场 ≥3 回合。</summary>
public sealed class ZhangKeXiEEffect : ISkillEffect
{
    public int GetPriorityTier(SkillCastContext ctx) => 0;

    public void Validate(SkillCastContext ctx)
    {
        if (ctx.Caster.HeroTime < 3)
            throw new RuleViolationException("tide_herotime");
    }

    public void Execute(SkillCastContext ctx)
    {
        var session = ctx.Session;
        var caster = ctx.Caster;

        // 永久成长：冰雪十字与效果二各 +2
        caster.GetSkill(SkillSlot.Q)!.AddState("bonus", 2);
        var e = caster.GetSkill(SkillSlot.E)!;
        e.AddState("bonus2", 2);
        session.ApplyBuff(caster, session.Catalog.GetBuff("tide_choice"), 1, -1, BuffApplyMode.Keep);

        int p = session.Rng.Next(3);
        switch (p)
        {
            case 0:
                // 下两回合冰雪十字额外 +4 魔法伤害
                session.ApplyBuff(caster, session.Catalog.GetBuff("tide_choice_q"), 1, 3, BuffApplyMode.Refresh);
                break;
            case 1:
                // 立即造成 (8+效果二) 魔法伤害
                if (ctx.Target is not null && !ctx.Target.IsDead)
                    SkillHelpers.DealMagic(session, caster, ctx.Target, (int)(8 + e.GetState("bonus2")));
                break;
            case 2:
                // 双抗各 +4 持续 3 回合
                session.ApplyBuff(caster, session.Catalog.GetBuff("tide_choice_def"), 1, 3, BuffApplyMode.Refresh);
                session.ApplyBuff(caster, session.Catalog.GetBuff("tide_choice_mdf"), 1, 3, BuffApplyMode.Refresh);
                break;
        }
    }
}

// ==================== 郑心予（英雄 9） ====================

/// <summary>礼赞（Q）：仅双数回合；标记下一次魔法伤害扣对方 42% 最大魔法值（不可叠加）。</summary>
public sealed class ZhengXinYuQEffect : ISkillEffect
{
    public int GetPriorityTier(SkillCastContext ctx) => 0;

    public void Validate(SkillCastContext ctx)
    {
        if (ctx.Session.Round % 2 != 0)
            throw new RuleViolationException("praise_even_round_only");
        var q = ctx.Caster.GetSkill(SkillSlot.Q)!;
        if (q.GetState("praise_active") > 0)
            throw new RuleViolationException("praise_active");
    }

    public void Execute(SkillCastContext ctx)
    {
        var session = ctx.Session;
        var caster = ctx.Caster;
        caster.GetSkill(SkillSlot.Q)!.SetState("praise_active", 1);
        session.ApplyBuff(caster, session.Catalog.GetBuff("praise"), 1, -1, BuffApplyMode.Keep);
    }
}

/// <summary>流星（W）：仅单数回合（结晶1 解除）；12 点魔法伤害（结晶2：20）。</summary>
public sealed class ZhengXinYuWEffect : ISkillEffect
{
    public int GetPriorityTier(SkillCastContext ctx) => 0;

    public void Validate(SkillCastContext ctx)
    {
        bool unlocked = ctx.Caster.CrystalActive && ctx.Caster.CrystalBranch == 1;
        if (!unlocked && ctx.Session.Round % 2 == 0)
            throw new RuleViolationException("meteor_odd_round_only");
        if (ctx.Target is null || ctx.Target.IsDead)
            throw new RuleViolationException("no_target");
    }

    public void Execute(SkillCastContext ctx)
    {
        var session = ctx.Session;
        bool crystal2 = ctx.Caster.CrystalActive && ctx.Caster.CrystalBranch == 2;
        int damage = crystal2 ? 20 : 12;
        SkillHelpers.DealMagic(session, ctx.Caster, ctx.Target!, damage);
    }
}

/// <summary>予恋之花（E）：自身 80% 物理减伤 + 敌方施法不能，持续 2 回合（可叠加）。减伤回合自身无法魔法回复。</summary>
public sealed class ZhengXinYuEEffect : ISkillEffect
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

        session.ApplyBuff(caster, session.Catalog.GetBuff("love_flower_user"), 1, 2, BuffApplyMode.Extend);
        session.ApplyBuff(target, session.Catalog.GetBuff("love_flower_enemy"), 1, 2, BuffApplyMode.Extend);
    }
}

/// <summary>心源神域（R）：2 回合（可叠加）；回合开始回复 [当前魔法值/2, 当前魔法值) 的生命。</summary>
public sealed class ZhengXinYuREffect : ISkillEffect
{
    public int GetPriorityTier(SkillCastContext ctx) => 0;

    public void Validate(SkillCastContext ctx)
    {
    }

    public void Execute(SkillCastContext ctx)
    {
        var session = ctx.Session;
        session.ApplyBuff(ctx.Caster, session.Catalog.GetBuff("heart_realm"), 1, 3, BuffApplyMode.Extend);
    }
}

// ==================== 刘珂明（英雄 10） ====================

/// <summary>剑舞（Q）：连续两次普攻，第二次伤害减半。</summary>
public sealed class LiuKeMingQEffect : ISkillEffect
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

        DamageCalculator.BasicAttack(session, caster, target);
        DamageCalculator.BasicAttack(session, caster, target, swordDanceHalf: true);
    }
}

/// <summary>月光剑（W）：45% 最大生命的物理伤害（受护甲/物穿削减）；施法者本回合无法魔法回复。</summary>
public sealed class LiuKeMingWEffect : ISkillEffect
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

        double armorPen = StatsResolver.ArmorPenetration(session, caster);
        double defense = StatsResolver.Defense(session, target);
        int d = Math.Max(0, (int)Math.Round(target.Stats.MaxHp * 0.45 - (1 - armorPen) * defense));

        DamageCalculator.PhysicalSkill(session, caster, target, d);

        // 施法者本回合无法获得魔法回复（原版 lmW 标记在施法者身上）
        caster.MpRegenBlocked = true;
    }
}
