namespace Zytter.Core.Data;

/// <summary>
/// 技能静态定义（数据驱动，来源于原版 MySQL skills 表）。
/// 效果本身不再像旧版那样散落在 7000 行 if/switch 里，
/// 而是通过 <see cref="EffectKey"/> 注册到技能效果管线（见 Skills 命名空间）。
/// </summary>
public sealed record SkillDefinition
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Hero { get; init; } = "";

    /// <summary>技能描述（原版数据库文案，已转换为纯文本换行）。</summary>
    public string Describe { get; init; } = "";

    /// <summary>魔法消耗。可被技能效果动态修改（如冰雪十字成长、结晶分支）。</summary>
    public int Mp { get; init; }

    /// <summary>效果管线注册键（如 yy_q / lxs_e）。</summary>
    public string Effect { get; init; } = "";

    public SkillTarget Target { get; init; } = SkillTarget.Enemy;

    public override string ToString() => $"{Name}(#{Id})";
}
