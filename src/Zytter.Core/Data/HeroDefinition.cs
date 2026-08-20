using Zytter.Core.Heroes;

namespace Zytter.Core.Data;

/// <summary>技能槽位。旧版为 Q/W/E/R 四个硬编码槽。</summary>
public enum SkillSlot
{
    Q,
    W,
    E,
    R,
}

/// <summary>技能目标规则（供 UI 提示与效果管线使用）。</summary>
public enum SkillTarget
{
    /// <summary>以敌方当前英雄为目标。</summary>
    Enemy,

    /// <summary>以自身为目标。</summary>
    Self,

    /// <summary>特殊目标规则（如洁净之灵按回合奇偶决定，汐之抉择随机三选一）。</summary>
    Special,
}

/// <summary>
/// 英雄静态定义（数据驱动，来源于原版 MySQL heroes 表）。
/// 战斗中的可变状态见 <see cref="Combatant"/>，两者职责分离——
/// 旧版 Hero 类身兼数据库行/UI 面板数据/战斗状态三职，是屎山的重要来源。
/// </summary>
public sealed class HeroDefinition
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Ename { get; init; } = "";
    public int Hp { get; init; }
    public int Mp { get; init; }
    public int Atk { get; init; }
    public int Def { get; init; }
    public int Adf { get; init; }

    /// <summary>行动力（旧 move 字段），决定结算先后。</summary>
    public int Move { get; init; }

    /// <summary>每回合魔法回复（旧 remp 字段）。</summary>
    public int Remp { get; init; }

    /// <summary>每回合生命回复（旧版基础值为 0，由装备/技能提供）。</summary>
    public int Hpp { get; init; }

    public int? Q { get; init; }
    public int? W { get; init; }
    public int? E { get; init; }
    public int? R { get; init; }

    /// <summary>是否拥有结晶之力系统（英雄 1/6/9/11）。</summary>
    public bool Crystal { get; init; }

    public IReadOnlyList<int> SkillIds
    {
        get
        {
            var list = new List<int>(4);
            if (Q is { } q) list.Add(q);
            if (W is { } w) list.Add(w);
            if (E is { } e) list.Add(e);
            if (R is { } r) list.Add(r);
            return list;
        }
    }

    public HeroStats CreateStats() => new(Hp, Mp, Atk, Def, Adf, Move, Hpp, Remp, 0, 0, 0);
}
