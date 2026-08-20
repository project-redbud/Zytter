namespace Zytter.Core.Heroes;

/// <summary>
/// 战斗状态位。对应旧版 Hero 上的 5 个 boolean 字段
/// （isgone/islimte/isatk/isskill/isfight），旧版散落各处手动翻转，
/// 新版集中为标志位集合，由 Buff/技能效果声明式施加。
/// </summary>
[Flags]
public enum CombatStatus : byte
{
    None = 0,

    /// <summary>完全行动不能（旧 isgone）：无法进行任何操作，如风之结界、冰雪十字。</summary>
    Incapacitated = 1,

    /// <summary>行动受限（旧 islimte）：如神谕，可行动但受限制。</summary>
    Limited = 2,

    /// <summary>攻击不能（旧 isatk）：无法进行普通攻击，如裂缝。</summary>
    Disarmed = 4,

    /// <summary>施法不能（旧 isskill）：无法释放技能，如予恋之花。</summary>
    Silenced = 8,

    /// <summary>战斗不能（旧 isfight）：如天圆地方。</summary>
    Pacified = 16,
}

public static class CombatStatusExtensions
{
    public static bool Has(this CombatStatus status, CombatStatus flag) => (status & flag) == flag;

    public static bool CanAct(this CombatStatus status) => !status.Has(CombatStatus.Incapacitated);

    public static bool CanBasicAttack(this CombatStatus status) =>
        status.CanAct() && !status.Has(CombatStatus.Disarmed) && !status.Has(CombatStatus.Pacified);

    public static bool CanCast(this CombatStatus status) =>
        status.CanAct() && !status.Has(CombatStatus.Silenced) && !status.Has(CombatStatus.Pacified);
}
