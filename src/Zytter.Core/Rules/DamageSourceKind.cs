namespace Zytter.Core.Rules;

/// <summary>
/// 伤害来源类别。原版对"装备被动伤害能否被禁卫军抵挡"等交互有特殊分支
/// （禁卫军不能抵挡紫月神杖/二阶红月神杖的伤害），来源类别用于这些规则判断。
/// </summary>
public enum DamageSourceKind
{
    /// <summary>普通攻击。</summary>
    BasicAttack,

    /// <summary>主动技能。</summary>
    Skill,

    /// <summary>装备被动（紫月神杖延迟伤害、二阶红月神杖附加伤害等）。</summary>
    EquipmentPassive,

    /// <summary>灼烧等持续伤害（奕阳烈日之箭）。</summary>
    Burn,

    /// <summary>真实伤害（无视一切减伤免疫）。</summary>
    True,
}
