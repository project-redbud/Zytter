using Zytter.Core.Battle;
using Zytter.Core.Data;
using Zytter.Core.Heroes;

namespace Zytter.Core.Buffs;

/// <summary>无引擎行为的 Buff（仅展示/由其他系统读取存在性）。</summary>
public sealed class NoOpBuffEffect : IBuffEffect
{
    public void Handle(BuffHook hook, BuffInstance buff, BuffContext ctx)
    {
    }
}

/// <summary>简单属性修正 Buff：StatQuery 挂点对生效值做固定量加法。</summary>
public sealed class StatModifierBuffEffect : IBuffEffect
{
    private readonly StatKind _kind;
    private readonly double _amount;

    public StatModifierBuffEffect(StatKind kind, double amount)
    {
        _kind = kind;
        _amount = amount;
    }

    public void Handle(BuffHook hook, BuffInstance buff, BuffContext ctx)
    {
        if (hook == BuffHook.StatQuery && ctx.StatQuery is { } query && query.Kind == _kind)
            query.Value += _amount;
    }
}

/// <summary>解放真名（刘晓释W）：每层 +2 攻击、+1 护甲；每层独立 10 回合到期扣回。</summary>
public sealed class LiberationBuffEffect : IBuffEffect
{
    public void Handle(BuffHook hook, BuffInstance buff, BuffContext ctx)
    {
        if (hook == BuffHook.StatQuery && ctx.StatQuery is { } query)
        {
            if (query.Kind == StatKind.Attack)
                query.Value += 2 * buff.Stacks;
            else if (query.Kind == StatKind.Defense)
                query.Value += 1 * buff.Stacks;
        }
    }
}

/// <summary>屠杀之风（奕阳E）：行动力 +2；到期时清零魔法伤害加成。</summary>
public sealed class SlaughterWindBuffEffect : IBuffEffect
{
    public void Handle(BuffHook hook, BuffInstance buff, BuffContext ctx)
    {
        if (hook == BuffHook.StatQuery && ctx.StatQuery is { Kind: StatKind.ActionPower } query)
        {
            query.Value += 2;
        }
        else if (hook == BuffHook.Removed)
        {
            // 到期扣回行动力加成与魔法伤害加成
            var e = ctx.Self.GetSkill(SkillSlot.E);
            e?.SetState("magic_bonus", 0);
        }
    }
}

/// <summary>云霄之巅（郈与却R）：攻击 +V1（结晶1：+6），行动力 +V2（结晶1：+2）。</summary>
public sealed class CloudTopBuffEffect : IBuffEffect
{
    public void Handle(BuffHook hook, BuffInstance buff, BuffContext ctx)
    {
        if (hook != BuffHook.StatQuery || ctx.StatQuery is not { } query) return;

        if (query.Kind == StatKind.Attack)
            query.Value += buff.V1;
        else if (query.Kind == StatKind.ActionPower)
            query.Value += buff.V2;
    }
}

/// <summary>圣歌（维多利娜W）：攻击 +3×层数 / 护甲 +2×层数。</summary>
public sealed class AnthemBuffEffect : IBuffEffect
{
    private readonly bool _attack;

    public AnthemBuffEffect(bool attack)
    {
        _attack = attack;
    }

    public void Handle(BuffHook hook, BuffInstance buff, BuffContext ctx)
    {
        if (hook != BuffHook.StatQuery || ctx.StatQuery is not { } query) return;
        if ((_attack && query.Kind == StatKind.Attack) || (!_attack && query.Kind == StatKind.Defense))
            query.Value += (_attack ? 3 : 2) * buff.Stacks;
    }
}

/// <summary>先入为主（郈与却Q）：普通模式魔穿 +30%（V2=0）；结晶3 模式伤害增强由伤害链读取 V1。</summary>
public sealed class FirstMoveBuffEffect : IBuffEffect
{
    public void Handle(BuffHook hook, BuffInstance buff, BuffContext ctx)
    {
        if (hook == BuffHook.StatQuery && ctx.StatQuery is { Kind: StatKind.MagicPenetration } query && buff.V2 == 0)
            query.Value += 0.3;
    }
}

/// <summary>心源神域（郑心予R）：回合开始回复 [当前魔法值/2, 当前魔法值) 的生命。修复原版 mp=0 崩溃。</summary>
public sealed class HeartRealmBuffEffect : IBuffEffect
{
    public void Handle(BuffHook hook, BuffInstance buff, BuffContext ctx)
    {
        if (hook != BuffHook.TurnStart) return;
        var combatant = ctx.Self;
        int mp = combatant.Stats.Mp;
        if (mp <= 0) return; // 原版 nextInt(0) 崩溃的修复

        int heal = ctx.Session.Rng.Next(mp / 2, mp);
        if (heal <= 0) return;
        int actual = combatant.Stats.AddHp(heal);
        if (actual > 0)
            ctx.Session.Emit(new HealedEvent(ctx.Session.NextSeq(), combatant.Side, combatant.Id, actual));
    }
}

/// <summary>予恋之花（敌方视角）：Buff 存在期间施法不能，到期恢复。</summary>
public sealed class LoveFlowerEnemyBuffEffect : IBuffEffect
{
    public void Handle(BuffHook hook, BuffInstance buff, BuffContext ctx)
    {
        var combatant = ctx.Self;
        switch (hook)
        {
            case BuffHook.Applied:
                if (!combatant.Status.Has(CombatStatus.Silenced))
                {
                    combatant.Status |= CombatStatus.Silenced;
                    ctx.Session.Emit(new StatusChangedEvent(ctx.Session.NextSeq(), combatant.Side, combatant.Id, combatant.Status));
                }
                break;
            case BuffHook.Removed:
                if (combatant.Status.Has(CombatStatus.Silenced))
                {
                    combatant.Status &= ~CombatStatus.Silenced;
                    ctx.Session.Emit(new StatusChangedEvent(ctx.Session.NextSeq(), combatant.Side, combatant.Id, combatant.Status));
                }
                break;
        }
    }
}

/// <summary>
/// 注册全部 Buff 效果。持续回合采用原版代码计数（回合开始递减），
/// 行为与原版 discountbuff 逐项对照（含"心源神域先回血后到期"等细节）。
/// </summary>
public static class BuffEffectsInstaller
{
    public static void Install(BattleSession session)
    {
        var reg = session.BuffEffects;

        reg.Register("liberation", new LiberationBuffEffect());
        reg.Register("exploitation", new StatModifierBuffEffect(StatKind.Defense, -4));
        reg.Register("star_fall", new StatModifierBuffEffect(StatKind.MagicDefense, -2));
        reg.Register("slaughter_wind", new SlaughterWindBuffEffect());
        reg.Register("cloud_top", new CloudTopBuffEffect());
        reg.Register("flash_plus_mdf", new StatModifierBuffEffect(StatKind.MagicDefense, 2));
        reg.Register("tide_choice_def", new StatModifierBuffEffect(StatKind.Defense, 4));
        reg.Register("tide_choice_mdf", new StatModifierBuffEffect(StatKind.MagicDefense, 4));
        reg.Register("resist_patch_def", new StatModifierBuffEffect(StatKind.Defense, 4));
        reg.Register("resist_patch_mdf", new StatModifierBuffEffect(StatKind.MagicDefense, 4));
        reg.Register("power_potion", new StatModifierBuffEffect(StatKind.Attack, 4));
        reg.Register("ap_capsule", new StatModifierBuffEffect(StatKind.ActionPower, 4));
        reg.Register("mp_filler_iii", new StatModifierBuffEffect(StatKind.MpRegen, 2));
        reg.Register("anthem_atk", new AnthemBuffEffect(true));
        reg.Register("anthem_def", new AnthemBuffEffect(false));
        reg.Register("rift_atk", new StatModifierBuffEffect(StatKind.Attack, 3));
        reg.Register("first_move", new FirstMoveBuffEffect());
        reg.Register("heart_realm", new HeartRealmBuffEffect());
        reg.Register("love_flower_enemy", new LoveFlowerEnemyBuffEffect());

        // 无引擎行为：效果在伤害链/状态机内实现，Buff 仅作存在性标记与展示
        var noOp = new NoOpBuffEffect();
        foreach (var buffId in new[]
                 {
                     "burn", "star_rush_steal", "star_rush_stolen", "wind_barrier_stun", "wind_barrier_lim",
                     "flash", "flash_plus", "ice_cross", "ice_wings", "hourglass", "tide_choice", "tide_choice_q",
                     "praise", "love_flower_user", "rift", "light_sword", "princess_order", "oracle", "revival",
                 })
        {
            reg.Register(buffId, noOp);
        }
    }
}
