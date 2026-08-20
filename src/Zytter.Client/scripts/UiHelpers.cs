using Zytter.Core.Data;

namespace Zytter.Client;

/// <summary>UI 公共工具：英雄/技能说明文本。</summary>
public static class UiHelpers
{
    /// <summary>
    /// 按宽度自动换行（Godot 默认 Tooltip 不换行，长行会被截断）。
    /// 中文字符按 2 个单位计，width 约等于半角字符数。
    /// </summary>
    public static string Wrap(string text, int width = 44)
    {
        var result = new System.Text.StringBuilder();
        int current = 0;
        foreach (var ch in text)
        {
            result.Append(ch);
            int w = ch > 127 ? 2 : 1; // CJK 按 2 单位
            current += w;
            if (ch == '\n')
            {
                current = 0;
            }
            else if (current >= width)
            {
                result.Append('\n');
                current = 0;
            }
        }
        return result.ToString();
    }

    public static string HeroTooltip(int heroId)
    {
        var catalog = GameDataCatalog.LoadDefault();
        var hero = catalog.GetHero(heroId);
        var parts = new List<string>
        {
            $"{hero.Name}（{hero.Ename}）",
            $"生命 {hero.Hp}  魔法 {hero.Mp}",
            $"攻击 {hero.Atk}  护甲 {hero.Def}  魔抗 {hero.Adf}",
            $"行动力 {hero.Move}  每回合回蓝 {hero.Remp}",
            "技能：",
        };
        foreach (var slot in new[] { SkillSlot.Q, SkillSlot.W, SkillSlot.E, SkillSlot.R })
        {
            var skill = catalog.GetSkill(hero, slot);
            if (skill is not null)
                parts.Add($"  [{slot}] {skill.Name}（{skill.Mp} 蓝）：{skill.Describe.Replace('\n', ' ')}");
        }
        return Wrap(string.Join("\n", parts));
    }

    public static string SkillTooltip(SkillDefinition skill) =>
        Wrap($"{skill.Name}（魔法消耗 {skill.Mp}）\n{skill.Describe}");

    public static string ItemTooltip(ItemDefinition item, int ownedCount = -1)
    {
        string kind = item.Kind == ItemKind.Consumable ? "消耗品" : "装备";
        string owned = ownedCount > 0 ? $"\n道具盒内已拥有：{ownedCount} 件" : "";
        return Wrap($"{item.Name}（{item.Gold} 金币，{kind}）\n{item.Describe}{owned}");
    }
}
