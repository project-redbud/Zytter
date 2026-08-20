using Zytter.Core.Battle;
using Zytter.Core.Buffs;
using Zytter.Core.Common;
using Zytter.Core.Data;
using Zytter.Core.Heroes;
using Zytter.Core.Rules;

namespace Zytter.Core.Skills;

// ==================== 罗天杰（英雄 4） ====================

/// <summary>暴怒（Q）：攻击 +4 执行一次普攻后恢复。</summary>
public sealed class LuoTianJieQEffect : ISkillEffect
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
        caster.Stats.AddAttack(4);
        try
        {
            DamageCalculator.BasicAttack(session, caster, ctx.Target!);
        }
        finally
        {
            caster.Stats.AddAttack(-4);
        }
    }
}

/// <summary>闪现（W）：60% 闪避普攻，持续 3 回合（不叠加）。</summary>
public sealed class LuoTianJieWEffect : ISkillEffect
{
    public int GetPriorityTier(SkillCastContext ctx) => 0;

    public void Validate(SkillCastContext ctx)
    {
    }

    public void Execute(SkillCastContext ctx)
    {
        var session = ctx.Session;
        session.ApplyBuff(ctx.Caster, session.Catalog.GetBuff("flash"), 1, 3, BuffApplyMode.Keep);
    }
}

/// <summary>断骨剑（E）：自损 7 点生命，造成 3 倍普攻伤害的物理伤害，敌方生命保底 2。上场 ≥2 回合且 HP&gt;7。</summary>
public sealed class LuoTianJieEEffect : ISkillEffect
{
    public int GetPriorityTier(SkillCastContext ctx) => 0;

    public void Validate(SkillCastContext ctx)
    {
        if (ctx.Caster.HeroTime < 2)
            throw new RuleViolationException("bone_breaker_herotime");
        if (ctx.Caster.Stats.Hp <= 7)
            throw new RuleViolationException("bone_breaker_hp");
        if (ctx.Target is null || ctx.Target.IsDead)
            throw new RuleViolationException("no_target");
    }

    public void Execute(SkillCastContext ctx)
    {
        var session = ctx.Session;
        var caster = ctx.Caster;

        // 自损 7 点生命（无下限保护，可自杀）
        caster.Stats.AddHp(-7);
        session.Emit(new DamageDealtEvent(session.NextSeq(), caster.Side, caster.Id, 7, DamageType.True));

        DamageCalculator.BoneBreaker(session, caster, ctx.Target!);
    }
}

// ==================== 郈与却（英雄 6） ====================

/// <summary>先入为主（Q）：魔穿 +30% 持续 3 回合（叠加刷新）；结晶3：技能伤害 +30%（每层 0.3）。</summary>
public sealed class HouYuQueQEffect : ISkillEffect
{
    public int GetPriorityTier(SkillCastContext ctx) => 0;

    public void Validate(SkillCastContext ctx)
    {
    }

    public void Execute(SkillCastContext ctx)
    {
        var session = ctx.Session;
        var caster = ctx.Caster;
        bool damageMode = caster.CrystalActive && caster.CrystalBranch == 3;

        var buff = session.ApplyBuff(caster, session.Catalog.GetBuff("first_move"), 1, 3, BuffApplyMode.Refresh);
        if (damageMode)
        {
            buff.V1 += 0.3; // hyqJ 累计
            buff.V2 = 1;    // 结晶3 模式标记
        }
        else
        {
            buff.V1 = 0.3;
            buff.V2 = 0;
        }
    }
}

/// <summary>强力剥削（W）：敌方护甲 -4 持续 3 回合（叠加刷新）。</summary>
public sealed class HouYuQueWEffect : ISkillEffect
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
        session.ApplyBuff(ctx.Target!, session.Catalog.GetBuff("exploitation"), 1, 3, BuffApplyMode.Refresh);
    }
}

/// <summary>星月奇迹（E）：8 点魔法伤害；结晶2：14 点且无视行动力（先手）。</summary>
public sealed class HouYuQueEEffect : ISkillEffect
{
    /// <summary>结晶2：无视行动力（先手特权）。</summary>
    public int GetPriorityTier(SkillCastContext ctx) =>
        ctx.Caster.CrystalActive && ctx.Caster.CrystalBranch == 2 ? 999 : 0;

    public void Validate(SkillCastContext ctx)
    {
        if (ctx.Target is null || ctx.Target.IsDead)
            throw new RuleViolationException("no_target");
    }

    public void Execute(SkillCastContext ctx)
    {
        var session = ctx.Session;
        bool crystal2 = ctx.Caster.CrystalActive && ctx.Caster.CrystalBranch == 2;
        int damage = crystal2 ? 14 : 8;
        SkillHelpers.DealMagic(session, ctx.Caster, ctx.Target!, damage);
    }
}

/// <summary>云霄之巅（R）：无视行动力 10 点魔法伤害；下 2 回合攻击 +4（结晶1：+6 且行动力 +2）。</summary>
public sealed class HouYuQueREffect : ISkillEffect
{
    public int GetPriorityTier(SkillCastContext ctx) => 999; // 无视行动力

    public void Validate(SkillCastContext ctx)
    {
        if (ctx.Target is null || ctx.Target.IsDead)
            throw new RuleViolationException("no_target");
    }

    public void Execute(SkillCastContext ctx)
    {
        var session = ctx.Session;
        var caster = ctx.Caster;

        SkillHelpers.DealMagic(session, caster, ctx.Target!, 10);

        bool crystal1 = caster.CrystalActive && caster.CrystalBranch == 1;
        var buff = session.ApplyBuff(caster, session.Catalog.GetBuff("cloud_top"), 1, 3, BuffApplyMode.Refresh);
        buff.V1 = crystal1 ? 6 : 4;
        buff.V2 = crystal1 ? 2 : 0;
    }
}

// ==================== 谢悠涵（英雄 7） ====================

/// <summary>
/// 洁净之灵（Q）：无视行动力。耗蓝 = 洁净点。
/// 单数回合：回合末回复（洁净点+4）生命；双数回合：造成等同洁净点的真实伤害。施放后洁净点清零。
/// </summary>
public sealed class XieYouHanQEffect : ISkillEffect
{
    public int GetPriorityTier(SkillCastContext ctx) => 9999; // 最高先手特权

    public void Validate(SkillCastContext ctx)
    {
        var q = ctx.Caster.GetSkill(SkillSlot.Q)!;
        if (q.GetState("purity") < 1)
            throw new RuleViolationException("no_purity");
    }

    public void Execute(SkillCastContext ctx)
    {
        var session = ctx.Session;
        var caster = ctx.Caster;
        var q = caster.GetSkill(SkillSlot.Q)!;
        int purity = (int)q.GetState("purity");

        q.SetState("purity", 0);
        q.Definition = q.Definition with { Mp = 0 };
        session.EmitSkillInfo(caster.Side, "purity", 0);

        if (session.Round % 2 == 1)
        {
            // 单数回合：回合末回复 洁净点+4
            q.SetState("heal_pending", purity + 4);
        }
        else
        {
            // 双数回合：真实伤害
            if (ctx.Target is not null && !ctx.Target.IsDead)
                DamageCalculator.True(session, ctx.Target, purity);
        }
    }
}

/// <summary>天圆地方（W）：10 点魔法伤害；敌方下一回合战斗不能。</summary>
public sealed class XieYouHanWEffect : ISkillEffect
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

        SkillHelpers.DealMagic(session, ctx.Caster, target, 10);
        if (session.TryApplyControl(target, CombatStatus.Pacified, "天圆地方"))
            session.ApplyBuff(target, session.Catalog.GetBuff("round_square"), 1, -1, BuffApplyMode.Keep);
    }
}

/// <summary>
/// 时光沙漏（E）：3 回合完全免疫伤害（魔王怒除外），累计伤害到期以 160% 回敬；
/// 被战斗/行动类控制打断时立即以 80% 释放。上场 ≥3 回合，生效期间不可重复。
/// </summary>
public sealed class XieYouHanEEffect : ISkillEffect
{
    public const double ReleaseRatio = 1.6;
    public const double InterruptedRatio = 0.8;

    public int GetPriorityTier(SkillCastContext ctx) => 0;

    public void Validate(SkillCastContext ctx)
    {
        if (ctx.Caster.HeroTime < 3)
            throw new RuleViolationException("hourglass_herotime");
        if (ctx.Caster.Buffs.Has("hourglass"))
            throw new RuleViolationException("hourglass_active");
    }

    public void Execute(SkillCastContext ctx)
    {
        var session = ctx.Session;
        var caster = ctx.Caster;

        caster.State["hourglass_rounds"] = 4; // 结算末递减，覆盖接下来 3 个完整回合
        session.ApplyBuff(caster, session.Catalog.GetBuff("hourglass"), 1, -1, BuffApplyMode.Keep);
    }
}
