using System.Diagnostics.CodeAnalysis;
using Zytter.Core.Buffs;
using Zytter.Core.Data;
using Zytter.Core.Heroes;
using Zytter.Core.Skills;

namespace Zytter.Core.Battle;

/// <summary>
/// 战斗单位（上场中的一名英雄）。
/// 旧版 Hero 实体同时充当数据库行/UI 面板/战斗状态，且每个英雄的技能状态都是
/// 该类上的公共字段；新版 Combatant 是纯粹的"战斗中单位"：
/// 静态定义（HeroDefinition）+ 可变属性快照（HeroStats）+ 状态位 + 技能运行时 + Buff 容器 + 装备。
/// </summary>
public sealed class Combatant
{
    /// <summary>对局内唯一编号（用于事件流与客户端引用）。</summary>
    public required int Id { get; init; }

    public required BattleSide Side { get; init; }

    public required HeroDefinition Hero { get; init; }

    public HeroStats Stats { get; }

    /// <summary>控制状态位（行动不能/受限/攻击不能/施法不能/战斗不能）。</summary>
    public CombatStatus Status { get; set; } = CombatStatus.None;

    public BuffContainer Buffs { get; } = new();

    /// <summary>按槽位组织的技能运行时（无该槽位则为 null，如杨圣诺无 E/R）。</summary>
    public IReadOnlyDictionary<SkillSlot, SkillRuntime> Skills { get; }

    /// <summary>装备（Z/X 两槽 + 已"吃"的永久装备）。</summary>
    public Equipment Equipment { get; }

    /// <summary>该英雄上场后经历的回合数（原版 herotime：断骨剑/时光沙漏/汐之抉择限制、结晶之力激活）。</summary>
    public int HeroTime { get; set; }

    /// <summary>该英雄上场后累计造成的伤害（结晶之力激活条件 damage>=20）。</summary>
    public double DamageDealt { get; set; }

    /// <summary>结晶之力是否已激活。</summary>
    public bool CrystalActive { get; set; }

    /// <summary>结晶之力分支（1/2/3，激活后由玩家选择）。</summary>
    public int CrystalBranch { get; set; }

    /// <summary>本回合魔法回复被封锁（破军之矛重伤/月光剑/予恋之花减伤），回合末结算后清除。</summary>
    public bool MpRegenBlocked { get; set; }

    /// <summary>
    /// 战斗单位级通用状态（装备被动等不属于某个技能的状态：
    /// 破军之矛冷却 "pojun_cd"、月光剑封锁 "moonlight_block" 等）。
    /// </summary>
    public Dictionary<string, double> State { get; } = new(StringComparer.Ordinal);

    /// <summary>是否处于时光沙漏状态（免疫伤害，魔王怒除外）。</summary>
    public bool IsInHourglass => Buffs.Has("hourglass");

    public bool IsDead => Stats.IsDead;

    [SetsRequiredMembers]
    public Combatant(int id, BattleSide side, HeroDefinition hero, HeroStats stats,
        IReadOnlyDictionary<SkillSlot, SkillRuntime> skills, GameDataCatalog catalog)
    {
        Id = id;
        Side = side;
        Hero = hero;
        Stats = stats;
        Skills = skills;
        Equipment = new Equipment(catalog);
    }

    public SkillRuntime? GetSkill(SkillSlot slot) =>
        Skills.TryGetValue(slot, out var skill) ? skill : null;

    /// <summary>行动力（生效值）：基础行动力 + Buff/装备修正（行动力胶囊、屠杀之风、紫月神杖、会徽）。</summary>
    public double EffectiveActionPower => Stats.ActionPower + Equipment.StatBonuses.Xdl;
}
