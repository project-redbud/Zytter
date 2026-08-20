namespace Zytter.Core.Data;

/// <summary>
/// Buff 静态定义（图标/名称/描述）。行为由 Buff 效果管线按 Id 注册实现；
/// 持续回合数/层数是运行时概念（见 Buffs.BuffInstance）。
/// </summary>
public sealed class BuffDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Desc { get; init; } = "";

    public override string ToString() => $"【{Name}】";
}
