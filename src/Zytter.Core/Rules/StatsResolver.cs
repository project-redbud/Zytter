using Zytter.Core.Battle;
using Zytter.Core.Buffs;

namespace Zytter.Core.Rules;

/// <summary>
/// 生效属性解析：基础值 + 装备加成 + Buff 修正（StatQuery 挂点）。
/// 旧版直接修改英雄属性字段并在到期时手动扣回（遗忘扣回即产生 bug），
/// 新版临时修正全部走 Buff，永久修正才改基础值（HeroStats.Adjust*）。
/// </summary>
public static class StatsResolver
{
    public static double Get(BattleSession session, Combatant combatant, StatKind kind)
    {
        var equipment = combatant.Equipment.StatBonuses;
        double baseValue = kind switch
        {
            StatKind.Attack => combatant.Stats.Attack + equipment.Atk,
            StatKind.Defense => combatant.Stats.Defense + equipment.Def,
            StatKind.MagicDefense => combatant.Stats.MagicDefense + equipment.Adf,
            StatKind.ActionPower => combatant.Stats.ActionPower + equipment.Xdl,
            StatKind.HpRegen => combatant.Stats.HpRegen + equipment.Hpp,
            StatKind.MpRegen => combatant.Stats.MpRegen + equipment.Mpp,
            StatKind.ArmorPenetration => combatant.Stats.ArmorPenetration + equipment.Adp,
            StatKind.MagicPenetration => combatant.Stats.MagicPenetration + equipment.App,
            StatKind.PhysicalDamageReduction => combatant.Stats.PhysicalDamageReduction,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        var payload = new StatQueryPayload { Kind = kind, Value = baseValue };
        session.ExecuteBuffs(BuffHook.StatQuery, new BuffContext
        {
            Session = session,
            Self = combatant,
            StatQuery = payload,
        });
        return payload.Value;
    }

    // ---- 常用便捷访问器 ----

    public static double Attack(BattleSession s, Combatant c) => Get(s, c, StatKind.Attack);

    public static double Defense(BattleSession s, Combatant c) => Get(s, c, StatKind.Defense);

    public static double MagicDefense(BattleSession s, Combatant c) => Get(s, c, StatKind.MagicDefense);

    public static double ActionPower(BattleSession s, Combatant c) => Get(s, c, StatKind.ActionPower);

    public static double HpRegen(BattleSession s, Combatant c) => Get(s, c, StatKind.HpRegen);

    public static double MpRegen(BattleSession s, Combatant c) => Get(s, c, StatKind.MpRegen);

    public static double ArmorPenetration(BattleSession s, Combatant c) => Get(s, c, StatKind.ArmorPenetration);

    public static double MagicPenetration(BattleSession s, Combatant c) => Get(s, c, StatKind.MagicPenetration);

    public static double PhysicalDamageReduction(BattleSession s, Combatant c) =>
        Get(s, c, StatKind.PhysicalDamageReduction);
}
