namespace Zytter.Core.Skills;

/// <summary>
/// 技能运行时实例：静态定义 + 该技能自己的战斗状态。
/// 旧版把每个英雄技能的状态硬编码为 Hero 上的几十个公共字段（yyQ、lxsE、zkxQ…），
/// 新版每个技能槽一个运行时对象，状态封闭在技能内部，随英雄换人整体重建。
/// </summary>
public sealed class SkillRuntime
{
    /// <summary>所属英雄（按英雄 ID 绑定，换人时整体重建）。</summary>
    public int HeroId { get; }

    /// <summary>当前技能定义（注意：耗蓝可被成长机制动态修改，如冰雪十字/界限突破）。</summary>
    public Data.SkillDefinition Definition { get; set; }

    /// <summary>技能的专属数值状态（如灼烧层数、洁净点、层数图标计数）。</summary>
    public Dictionary<string, double> State { get; } = new(StringComparer.Ordinal);

    public SkillRuntime(int heroId, Data.SkillDefinition definition)
    {
        HeroId = heroId;
        Definition = definition;
    }

    public double GetState(string key, double fallback = 0) =>
        State.TryGetValue(key, out var value) ? value : fallback;

    public void SetState(string key, double value) => State[key] = value;

    public void AddState(string key, double delta) => SetState(key, GetState(key) + delta);
}
