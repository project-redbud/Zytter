using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Zytter.Core.Common;

namespace Zytter.Core.Data;

/// <summary>
/// 静态游戏数据目录：英雄/技能/物品/Buff。
/// 数据以 JSON 形式嵌入程序集（服务器与 Godot 客户端零文件部署），
/// 数值调整只需改 Data/*.json 重新编译，或通过 FromJson 注入外部数据。
/// </summary>
public sealed class GameDataCatalog
{
    public IReadOnlyDictionary<int, HeroDefinition> Heroes { get; }
    public IReadOnlyDictionary<int, SkillDefinition> Skills { get; }
    public IReadOnlyDictionary<int, ItemDefinition> Items { get; }
    public IReadOnlyDictionary<string, BuffDefinition> Buffs { get; }

    public GameDataCatalog(
        IEnumerable<HeroDefinition> heroes,
        IEnumerable<SkillDefinition> skills,
        IEnumerable<ItemDefinition> items,
        IEnumerable<BuffDefinition> buffs)
    {
        Heroes = heroes.ToDictionary(h => h.Id);
        Skills = skills.ToDictionary(s => s.Id);
        Items = items.ToDictionary(i => i.Id);
        Buffs = buffs.ToDictionary(b => b.Id);
        Validate();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>加载嵌入程序集的默认数据（Data/*.json）。</summary>
    public static GameDataCatalog LoadDefault()
    {
        var assembly = typeof(GameDataCatalog).Assembly;
        return new GameDataCatalog(
            Read<List<HeroDefinition>>(assembly, "Zytter.Core.Data.heroes.json"),
            Read<List<SkillDefinition>>(assembly, "Zytter.Core.Data.skills.json"),
            Read<List<ItemDefinition>>(assembly, "Zytter.Core.Data.items.json"),
            Read<List<BuffDefinition>>(assembly, "Zytter.Core.Data.buffs.json"));
    }

    private static T Read<T>(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new GameDataException($"找不到嵌入资源 {resourceName}");
        return JsonSerializer.Deserialize<T>(stream, JsonOptions)
            ?? throw new GameDataException($"资源 {resourceName} 反序列化失败");
    }

    public HeroDefinition GetHero(int id) =>
        Heroes.TryGetValue(id, out var hero) ? hero : throw new GameDataException($"英雄 #{id} 不存在");

    public SkillDefinition GetSkill(int id) =>
        Skills.TryGetValue(id, out var skill) ? skill : throw new GameDataException($"技能 #{id} 不存在");

    public ItemDefinition GetItem(int id) =>
        Items.TryGetValue(id, out var item) ? item : throw new GameDataException($"物品 #{id} 不存在");

    public BuffDefinition GetBuff(string id) =>
        Buffs.TryGetValue(id, out var buff) ? buff : throw new GameDataException($"Buff {id} 不存在");

    /// <summary>取英雄某槽位的技能定义（英雄可能没有该槽位，如杨圣诺无 E/R）。</summary>
    public SkillDefinition? GetSkill(HeroDefinition hero, SkillSlot slot) => slot switch
    {
        SkillSlot.Q => hero.Q is { } q ? GetSkill(q) : null,
        SkillSlot.W => hero.W is { } w ? GetSkill(w) : null,
        SkillSlot.E => hero.E is { } e ? GetSkill(e) : null,
        SkillSlot.R => hero.R is { } r ? GetSkill(r) : null,
        _ => null,
    };

    /// <summary>数据完整性校验：技能/物品引用必须存在、技能效果键必须唯一非空。</summary>
    private void Validate()
    {
        foreach (var hero in Heroes.Values)
        {
            foreach (var skillId in hero.SkillIds)
            {
                if (!Skills.ContainsKey(skillId))
                    throw new GameDataException($"英雄 {hero.Name} 引用了不存在的技能 #{skillId}");
            }
        }

        var effectKeys = Skills.Values.Select(s => s.Effect).Where(e => e.Length > 0).ToList();
        var duplicates = effectKeys.GroupBy(e => e).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
            throw new GameDataException($"技能效果键重复：{string.Join(", ", duplicates)}");

        foreach (var skill in Skills.Values)
        {
            if (string.IsNullOrWhiteSpace(skill.Effect))
                throw new GameDataException($"技能 {skill.Name} 缺少效果键");
        }

        foreach (var item in Items.Values)
        {
            if (string.IsNullOrWhiteSpace(item.Effect))
                throw new GameDataException($"物品 {item.Name} 缺少效果键");
        }
    }
}
