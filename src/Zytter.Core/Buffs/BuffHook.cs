namespace Zytter.Core.Buffs;

/// <summary>
/// Buff 生命周期挂点。引擎在各关键时点遍历战斗单位的 Buff 容器触发对应挂点。
/// 旧版 Buff 是几十个 boolean 标志 + 线程轮询；新版为声明式挂点回调。
/// </summary>
public enum BuffHook
{
    /// <summary>Buff 施加到单位上时（可用于触发即时效果）。</summary>
    Applied,

    /// <summary>Buff 移除时（含到期、被净化、英雄死亡清理）。</summary>
    Removed,

    /// <summary>回合开始（对应旧版 discountbuff 倒计时与心源神域回血等）。</summary>
    TurnStart,

    /// <summary>回合结束（对应旧版 hppNmpp 结算，如紫月神杖延迟伤害）。</summary>
    TurnEnd,

    /// <summary>受到伤害前：可修改伤害值、免疫伤害（时光沙漏/冰之羽翼/予恋之花）。</summary>
    BeforeDamaged,

    /// <summary>受到伤害后：触发洁净点累计、禁卫军、礼赞、会徽吸血等。</summary>
    AfterDamaged,

    /// <summary>造成伤害后（紫月/二阶红月等装备被动）。</summary>
    AfterDealtDamage,

    /// <summary>行动选择校验前（如神谕强制行动规则）。</summary>
    BeforeAct,

    /// <summary>属性查询修正（行动力胶囊、强化药水等生效值计算）。</summary>
    StatQuery,
}

/// <summary>Buff 挂点回调上下文。</summary>
public sealed class BuffContext
{
    public required Battle.BattleSession Session { get; init; }

    /// <summary>Buff 宿主（承受该 Buff 的单位）。</summary>
    public required Battle.Combatant Self { get; init; }

    /// <summary>触发来源（如伤害来源），可为 null。</summary>
    public Battle.Combatant? Source { get; init; }

    /// <summary>伤害事件载荷（BeforeDamaged/AfterDamaged/AfterDealtDamage 时使用）。</summary>
    public DamagePayload? Damage { get; init; }

    /// <summary>属性查询载荷（StatQuery 时使用）。</summary>
    public StatQueryPayload? StatQuery { get; init; }
}

/// <summary>伤害事件载荷。Before 挂点可修改 <see cref="Value"/>（含免疫置 0）。</summary>
public sealed class DamagePayload
{
    public required DamageType Type { get; init; }

    /// <summary>待结算伤害值，Before 挂点可修改。</summary>
    public int Value { get; set; }

    /// <summary>是否已被完全免疫（免疫后不再进入后续减伤与扣血）。</summary>
    public bool Negated { get; set; }

    public required Rules.DamageSourceKind SourceKind { get; init; }

    /// <summary>剑舞第二刀等特殊标记。</summary>
    public bool IsHalved { get; set; }
}

/// <summary>伤害类型（与原版三段结算链对应）。</summary>
public enum DamageType
{
    /// <summary>普通攻击物理伤害。</summary>
    Physical,

    /// <summary>魔法伤害。</summary>
    Magical,

    /// <summary>真实伤害：无视一切减伤与免疫（洁净之灵双数回合）。</summary>
    True,
}

/// <summary>属性查询载荷：Buff 可对生效值做加法修正。</summary>
public sealed class StatQueryPayload
{
    public required StatKind Kind { get; init; }
    public double Value { get; set; }
}

/// <summary>可被 Buff 修正的属性。</summary>
public enum StatKind
{
    Attack,
    Defense,
    MagicDefense,
    ActionPower,
    HpRegen,
    MpRegen,
    ArmorPenetration,
    MagicPenetration,
    PhysicalDamageReduction,
}
