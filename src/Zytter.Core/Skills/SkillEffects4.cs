using Zytter.Core.Battle;
using Zytter.Core.Buffs;
using Zytter.Core.Common;
using Zytter.Core.Data;
using Zytter.Core.Heroes;
using Zytter.Core.Rules;

namespace Zytter.Core.Skills;

// ==================== 苏璟静（英雄 11） ====================

/// <summary>闪现+（Q）：无视行动力；80% 闪避普攻 + 魔抗 +2，持续 2 回合（结晶3：3 回合且可叠加）。</summary>
public sealed class SuJingJingQEffect : ISkillEffect
{
    public int GetPriorityTier(SkillCastContext ctx) => 999; // 无视行动力

    public void Validate(SkillCastContext ctx)
    {
    }

    public void Execute(SkillCastContext ctx)
    {
        var session = ctx.Session;
        var caster = ctx.Caster;
        bool crystal3 = caster.CrystalActive && caster.CrystalBranch == 3;

        int duration = crystal3 ? 3 : 2;
        var mode = crystal3 ? BuffApplyMode.Extend : BuffApplyMode.Keep;
        session.ApplyBuff(caster, session.Catalog.GetBuff("flash_plus"), 1, duration, mode);
        session.ApplyBuff(caster, session.Catalog.GetBuff("flash_plus_mdf"), 1, duration, mode);
    }
}

/// <summary>裂缝（W）：下回合自身攻击 +3；敌方战斗不能（下回合）。可提前击碎时光沙漏。</summary>
public sealed class SuJingJingWEffect : ISkillEffect
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

        session.ApplyBuff(caster, session.Catalog.GetBuff("rift_atk"), 1, 2, BuffApplyMode.Keep);
        if (session.TryApplyControl(target, CombatStatus.Pacified, "裂缝"))
            session.ApplyBuff(target, session.Catalog.GetBuff("rift"), 1, -1, BuffApplyMode.Keep);
    }
}

/// <summary>光炽剑（E）：标记下一次普攻命中：回复 3 点生命（结晶1：5）+ 6 点魔法伤害（结晶1：9）。不可叠加。</summary>
public sealed class SuJingJingEEffect : ISkillEffect
{
    public int GetPriorityTier(SkillCastContext ctx) => 0;

    public void Validate(SkillCastContext ctx)
    {
        if (ctx.Caster.Buffs.Has("light_sword"))
            throw new RuleViolationException("light_sword_active");
    }

    public void Execute(SkillCastContext ctx)
    {
        var session = ctx.Session;
        session.ApplyBuff(ctx.Caster, session.Catalog.GetBuff("light_sword"), 1, -1, BuffApplyMode.Keep);
    }
}

/// <summary>公主号令（R）：召唤 3 名禁卫军（可叠加），每名抵挡至多 4 点伤害。</summary>
public sealed class SuJingJingREffect : ISkillEffect
{
    public int GetPriorityTier(SkillCastContext ctx) => 0;

    public void Validate(SkillCastContext ctx)
    {
    }

    public void Execute(SkillCastContext ctx)
    {
        var session = ctx.Session;
        var caster = ctx.Caster;

        double guards = caster.GetSkill(SkillSlot.R)!.GetState("guards") + 3;
        caster.GetSkill(SkillSlot.R)!.SetState("guards", guards);
        session.ApplyBuff(caster, session.Catalog.GetBuff("princess_order"), 3, -1, BuffApplyMode.Keep);
        session.EmitSkillInfo(caster.Side, "guards", (int)guards);
    }
}

// ==================== 维多利娜（英雄 12） ====================

/// <summary>神谕（Q）：随机指定规则（普攻/技能/放弃），对方下一回合必须遵守，违反永久 -1 护甲。</summary>
public sealed class WeiDuoLiNaQEffect : ISkillEffect
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

        int p = session.Rng.Next(3); // 0=必须普攻 1=必须技能 2=必须放弃
        target.State["oracle_rule"] = p + 1;
        session.EmitSkillInfo(target.Side, "oracle", p + 1);
        session.ApplyBuff(target, session.Catalog.GetBuff("oracle"), 1, -1, BuffApplyMode.Keep);
    }
}

/// <summary>圣歌（W）：攻击 +3、护甲 +2 持续 3 回合（可叠加）；永久魔法回复 +1。</summary>
public sealed class WeiDuoLiNaWEffect : ISkillEffect
{
    public int GetPriorityTier(SkillCastContext ctx) => 0;

    public void Validate(SkillCastContext ctx)
    {
    }

    public void Execute(SkillCastContext ctx)
    {
        var session = ctx.Session;
        var caster = ctx.Caster;

        session.ApplyBuff(caster, session.Catalog.GetBuff("anthem_atk"), 1, 3, BuffApplyMode.Refresh);
        session.ApplyBuff(caster, session.Catalog.GetBuff("anthem_def"), 1, 3, BuffApplyMode.Refresh);
        caster.Stats.AddMpRegen(1); // 永久
    }
}

/// <summary>
/// 注册全部 36 个技能效果（按技能数据文件的 Effect 键）。
/// </summary>
public static class SkillEffectsInstaller
{
    public static void Install(BattleSession session)
    {
        var reg = session.SkillEffects;

        // 奕阳
        reg.Register("yy_q", new YiYangQEffect());
        reg.Register("yy_w", new YiYangWEffect());
        reg.Register("yy_e", new YiYangEEffect());
        // 刘晓释
        reg.Register("lxs_q", new LiuXiaoShiQEffect());
        reg.Register("lxs_w", new LiuXiaoShiWEffect());
        reg.Register("lxs_e", new LiuXiaoShiEEffect());
        // 杨圣诺
        reg.Register("ysn_q", new YangShengNuoQEffect());
        reg.Register("ysn_w", new YangShengNuoWEffect());
        // 张枫
        reg.Register("zf_q", new ZhangFengQEffect());
        reg.Register("zf_w", new ZhangFengWEffect());
        reg.Register("zf_e", new ZhangFengEEffect());
        // 罗天杰
        reg.Register("ltj_q", new LuoTianJieQEffect());
        reg.Register("ltj_w", new LuoTianJieWEffect());
        reg.Register("ltj_e", new LuoTianJieEEffect());
        // 郈与却
        reg.Register("hyq_q", new HouYuQueQEffect());
        reg.Register("hyq_w", new HouYuQueWEffect());
        reg.Register("hyq_e", new HouYuQueEEffect());
        reg.Register("hyq_r", new HouYuQueREffect());
        // 谢悠涵
        reg.Register("xyh_q", new XieYouHanQEffect());
        reg.Register("xyh_w", new XieYouHanWEffect());
        reg.Register("xyh_e", new XieYouHanEEffect());
        // 张可汐
        reg.Register("zkx_q", new ZhangKeXiQEffect());
        reg.Register("zkx_w", new ZhangKeXiWEffect());
        reg.Register("zkx_e", new ZhangKeXiEEffect());
        // 郑心予
        reg.Register("zxy_q", new ZhengXinYuQEffect());
        reg.Register("zxy_w", new ZhengXinYuWEffect());
        reg.Register("zxy_e", new ZhengXinYuEEffect());
        reg.Register("zxy_r", new ZhengXinYuREffect());
        // 刘珂明
        reg.Register("lm_q", new LiuKeMingQEffect());
        reg.Register("lm_w", new LiuKeMingWEffect());
        // 苏璟静
        reg.Register("sjj_q", new SuJingJingQEffect());
        reg.Register("sjj_w", new SuJingJingWEffect());
        reg.Register("sjj_e", new SuJingJingEEffect());
        reg.Register("sjj_r", new SuJingJingREffect());
        // 维多利娜
        reg.Register("w_q", new WeiDuoLiNaQEffect());
        reg.Register("w_w", new WeiDuoLiNaWEffect());
    }
}
