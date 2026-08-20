using Zytter.Core.Buffs;
using Zytter.Core.Data;
using Zytter.Core.Skills;

namespace Zytter.Core.Battle;

/// <summary>
/// Buff 行为接口：按 BuffId 注册到 <see cref="BuffEffectRegistry"/>。
/// 简单数值修正（强化药水等）与复杂机制（时光沙漏、冰之羽翼等）都实现此接口，
/// 引擎通过挂点统一驱动，不再需要任何 boolean 标志位。
/// </summary>
public interface IBuffEffect
{
    void Handle(BuffHook hook, BuffInstance buff, BuffContext ctx);
}

/// <summary>技能效果接口：校验 + 执行分离，先手特权由技能声明（替代旧版临时改行动力的 hack）。</summary>
public interface ISkillEffect
{
    /// <summary>
    /// 先手特权层级：0=正常按行动力；999=高优先（云霄之巅/星月奇迹结晶2/闪现+）；
    /// 9999=最高优先（洁净之灵）。层级相同再比行动力，行动力相同房主先手。
    /// 依赖施法者状态（如结晶分支）时可动态计算。
    /// </summary>
    int GetPriorityTier(SkillCastContext ctx);

    /// <summary>施放前校验（耗蓝、专属限制）。不可施放时抛出 <see cref="Common.RuleViolationException"/>。</summary>
    void Validate(SkillCastContext ctx);

    /// <summary>执行技能效果（扣蓝已在引擎统一处理）。</summary>
    void Execute(SkillCastContext ctx);
}

/// <summary>物品效果接口（消耗品使用/装备被动）。</summary>
public interface IItemEffect
{
    /// <summary>使用消耗品（战斗内使用操作码 13~22 对应 id 3~12）。</summary>
    void Use(ItemContext ctx);

    /// <summary>装备被动触发点（由伤害/回合管线调用）。</summary>
    void OnPassive(ItemPassiveHook hook, ItemContext ctx);
}

/// <summary>技能施放上下文。</summary>
public sealed class SkillCastContext
{
    public required BattleSession Session { get; init; }
    public required Combatant Caster { get; init; }
    public required Combatant? Target { get; init; }
    public required SkillSlot Slot { get; init; }
    public required SkillRuntime Runtime { get; init; }

    public SkillDefinition Definition => Runtime.Definition;
}

/// <summary>物品使用上下文。</summary>
public sealed class ItemContext
{
    public required BattleSession Session { get; init; }
    public required Combatant User { get; init; }
    public required int ItemId { get; init; }

    public ItemDefinition Definition => Session.Catalog.GetItem(ItemId);
}

/// <summary>装备被动触发点。</summary>
public enum ItemPassiveHook
{
    /// <summary>造成魔法伤害后（学生会会徽吸血、二阶红月/紫月附加伤害、冰雪十字成长）。</summary>
    OnMagicDamageDealt,

    /// <summary>造成物理伤害后（破军之矛重伤）。</summary>
    OnPhysicalDamageDealt,

    /// <summary>受到物理伤害结算前（坚韧者之盾减伤、予恋之花减伤）。</summary>
    OnPhysicalDamageTaken,

    /// <summary>受到强控制前（夜宴之声抵挡）。</summary>
    OnStrongControl,

    /// <summary>普通攻击伤害倍率（鹰角弓 1.42）。</summary>
    OnBasicAttackModifier,

    /// <summary>回合结束（紫月神杖延迟伤害）。</summary>
    TurnEnd,
}

public sealed class BuffEffectRegistry
{
    private readonly Dictionary<string, IBuffEffect> _effects = new(StringComparer.Ordinal);

    public void Register(string buffId, IBuffEffect effect) => _effects[buffId] = effect;

    public IBuffEffect? Get(string buffId) =>
        _effects.TryGetValue(buffId, out var effect) ? effect : null;
}

public sealed class SkillEffectRegistry
{
    private readonly Dictionary<string, ISkillEffect> _effects = new(StringComparer.Ordinal);

    public void Register(string effectKey, ISkillEffect effect) => _effects[effectKey] = effect;

    public ISkillEffect Get(string effectKey) =>
        _effects.TryGetValue(effectKey, out var effect)
            ? effect
            : throw new Common.GameDataException($"技能效果 {effectKey} 未注册");
}

public sealed class ItemEffectRegistry
{
    private readonly Dictionary<string, IItemEffect> _effects = new(StringComparer.Ordinal);

    public void Register(string effectKey, IItemEffect effect) => _effects[effectKey] = effect;

    public IItemEffect? Get(string effectKey) =>
        _effects.TryGetValue(effectKey, out var effect) ? effect : null;
}
