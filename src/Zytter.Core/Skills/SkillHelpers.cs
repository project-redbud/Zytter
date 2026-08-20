using Zytter.Core.Battle;
using Zytter.Core.Rules;

namespace Zytter.Core.Skills;

/// <summary>技能效果公共助手。</summary>
public static class SkillHelpers
{
    /// <summary>技能魔法伤害标准结算：基础值 → 魔抗削减 → 魔法链。</summary>
    public static void DealMagic(BattleSession session, Combatant caster, Combatant target, int baseDamage)
    {
        int d = DamageCalculator.ComputeMagicDamage(session, caster, target, baseDamage);
        DamageCalculator.Magic(session, caster, target, d, isEquipmentPassive: false);
    }

    /// <summary>
    /// 原版幸运数字判定：掷 p∈[0,9]，p &lt;= 阈值则成功。
    /// 同时广播 LuckRollEvent（客户端全屏展示幸运数字）。
    /// </summary>
    public static bool LuckyRoll(BattleSession session, Combatant caster, string skillName, int threshold)
    {
        int rolled = session.Rng.Next(10);
        bool success = rolled <= threshold;
        session.Emit(new LuckRollEvent(session.NextSeq(), caster.Side, skillName, rolled, threshold, success));
        return success;
    }

    /// <summary>行动力个位数（原版：<100 取个位，100~999 取后两位）。</summary>
    public static int ActionPowerDigit(double actionPower)
    {
        int x = (int)actionPower;
        return x < 100 ? x % 10 : x % 100;
    }
}
