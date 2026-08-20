using System.Text.Json.Serialization;
using Zytter.Core.Buffs;
using Zytter.Core.Heroes;

namespace Zytter.Core.Battle;

/// <summary>
/// 对局事件流。引擎的一切状态变更都以事件形式广播，
/// 客户端只消费事件做表现（旧版客户端自行模拟全部规则，改由服务器权威 + 事件驱动）。
/// 带 STJ 多态判别，SignalR JSON 传输可直接反序列化为具体子类型。
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(BattleStartedEvent), "battle_started")]
[JsonDerivedType(typeof(PhaseChangedEvent), "phase_changed")]
[JsonDerivedType(typeof(RoundStartedEvent), "round_started")]
[JsonDerivedType(typeof(ShopOpenedEvent), "shop_opened")]
[JsonDerivedType(typeof(ActionLockedEvent), "action_locked")]
[JsonDerivedType(typeof(ActionSkippedEvent), "action_skipped")]
[JsonDerivedType(typeof(BasicAttackEvent), "basic_attack")]
[JsonDerivedType(typeof(SkillCastEvent), "skill_cast")]
[JsonDerivedType(typeof(ItemUsedEvent), "item_used")]
[JsonDerivedType(typeof(DamageDealtEvent), "damage_dealt")]
[JsonDerivedType(typeof(HealedEvent), "healed")]
[JsonDerivedType(typeof(MpChangedEvent), "mp_changed")]
[JsonDerivedType(typeof(BuffAppliedEvent), "buff_applied")]
[JsonDerivedType(typeof(BuffRemovedEvent), "buff_removed")]
[JsonDerivedType(typeof(BuffSyncEvent), "buff_sync")]
[JsonDerivedType(typeof(StatusChangedEvent), "status_changed")]
[JsonDerivedType(typeof(HeroDiedEvent), "hero_died")]
[JsonDerivedType(typeof(HeroSwitchedEvent), "hero_switched")]
[JsonDerivedType(typeof(GoldChangedEvent), "gold_changed")]
[JsonDerivedType(typeof(ItemObtainedEvent), "item_obtained")]
[JsonDerivedType(typeof(ItemLostEvent), "item_lost")]
[JsonDerivedType(typeof(EquipmentChangedEvent), "equipment_changed")]
[JsonDerivedType(typeof(HeroStatsSyncEvent), "hero_stats_sync")]
[JsonDerivedType(typeof(LuckRollEvent), "luck_roll")]
[JsonDerivedType(typeof(SkillInfoEvent), "skill_info")]
[JsonDerivedType(typeof(RoundEndedEvent), "round_ended")]
[JsonDerivedType(typeof(CrystalReadyEvent), "crystal_ready")]
[JsonDerivedType(typeof(CrystalChosenEvent), "crystal_chosen")]
[JsonDerivedType(typeof(PauseStateChangedEvent), "pause_changed")]
[JsonDerivedType(typeof(SurrenderEvent), "surrender")]
[JsonDerivedType(typeof(DisconnectedEvent), "disconnected")]
[JsonDerivedType(typeof(BattleEndedEvent), "battle_ended")]
public abstract record BattleEvent(long Seq);

public sealed record BattleStartedEvent(long Seq, int RoundLimit) : BattleEvent(Seq);

public sealed record PhaseChangedEvent(long Seq, BattlePhase Phase, int RemainingSeconds) : BattleEvent(Seq);

public sealed record RoundStartedEvent(long Seq, int Round, int RoundLimit) : BattleEvent(Seq);

public sealed record ShopOpenedEvent(long Seq, BattleSide Side, int GoldGranted, int ShoppingSeconds) : BattleEvent(Seq);

public sealed record ActionLockedEvent(long Seq, BattleSide Side) : BattleEvent(Seq);

public sealed record ActionSkippedEvent(long Seq, BattleSide Side, string Reason) : BattleEvent(Seq);

/// <summary>普攻事件。DodgeThreshold=0 表示防守方无闪避能力；闪避判定为 DodgeRoll &lt; DodgeThreshold。</summary>
public sealed record BasicAttackEvent(long Seq, BattleSide Side, bool Dodged, int DodgeRoll, int DodgeThreshold) : BattleEvent(Seq);

public sealed record SkillCastEvent(long Seq, BattleSide Side, int SkillId, string SkillName, int MpCost) : BattleEvent(Seq);

public sealed record ItemUsedEvent(long Seq, BattleSide Side, int ItemId, string ItemName) : BattleEvent(Seq);

public sealed record DamageDealtEvent(long Seq, BattleSide TargetSide, int TargetCombatantId, int Amount, DamageType Type) : BattleEvent(Seq);

public sealed record HealedEvent(long Seq, BattleSide Side, int CombatantId, int Amount) : BattleEvent(Seq);

public sealed record MpChangedEvent(long Seq, BattleSide Side, int CombatantId, int Delta) : BattleEvent(Seq);

public sealed record BuffAppliedEvent(long Seq, BattleSide Side, int CombatantId, string BuffId, string BuffName, int Stacks, int DurationRounds) : BattleEvent(Seq);

public sealed record BuffRemovedEvent(long Seq, BattleSide Side, int CombatantId, string BuffId, string BuffName) : BattleEvent(Seq);

/// <summary>
/// 服务器权威同步：某方当前全部 Buff 的剩余持续回合（每回合开始广播）。
/// Rounds：buffId → 剩余回合；-1 = 无回合概念（永久/层数型，由其他机制展示）。
/// </summary>
public sealed record BuffSyncEvent(long Seq, BattleSide Side, int CombatantId, Dictionary<string, int> Rounds) : BattleEvent(Seq);

public sealed record StatusChangedEvent(long Seq, BattleSide Side, int CombatantId, CombatStatus Status) : BattleEvent(Seq);

public sealed record HeroDiedEvent(long Seq, BattleSide Side, int CombatantId, string HeroName) : BattleEvent(Seq);

public sealed record HeroSwitchedEvent(long Seq, BattleSide Side, string HeroName, int MaxHp, int MaxMp) : BattleEvent(Seq);

public sealed record GoldChangedEvent(long Seq, BattleSide Side, int Gold, int Delta) : BattleEvent(Seq);

/// <summary>道具盒获得物品。</summary>
public sealed record ItemObtainedEvent(long Seq, BattleSide Side, int ItemId, string ItemName, string Source) : BattleEvent(Seq);

/// <summary>道具盒失去物品。</summary>
public sealed record ItemLostEvent(long Seq, BattleSide Side, int ItemId, string ItemName, string Reason) : BattleEvent(Seq);

/// <summary>装备槽变化（ItemId 为 null 表示脱下）。</summary>
public sealed record EquipmentChangedEvent(long Seq, BattleSide Side, string Slot, int? ItemId) : BattleEvent(Seq);

/// <summary>
/// 英雄属性权威同步（每回合结束由服务器广播双方当前数值）。
/// 客户端以该事件为准校正本地显示，杜绝"界限突破提上限""商店成长加血"等
/// 服务器侧数值变化未同步导致的显示漂移。
/// </summary>
public sealed record HeroStatsSyncEvent(
    long Seq, BattleSide Side, int CombatantId, string HeroName, int HeroId,
    int Hp, int MaxHp, int Mp, int MaxMp,
    double Attack, double Defense, double MagicDefense, double ActionPower) : BattleEvent(Seq);

/// <summary>
/// 幸运数字判定（奕阳 Q/W/E、魔王怒、风之结界 70% 等随机判定）：
/// 掷得 Rolled，阈值为 Threshold（≤阈值即成功），供客户端全屏展示。
/// </summary>
public sealed record LuckRollEvent(
    long Seq, BattleSide Side, string SkillName, int Rolled, int Threshold, bool Success) : BattleEvent(Seq);

/// <summary>
/// 技能状态信息（供客户端 tooltip 展示）：
/// purity=谢悠涵洁净点；kill_chance=刘晓释魔王怒概率；oracle=维多利娜神谕规则（1普攻/2技能/3放弃）。
/// </summary>
public sealed record SkillInfoEvent(long Seq, BattleSide Side, string Key, int Value) : BattleEvent(Seq);

public sealed record RoundEndedEvent(long Seq, int Round, int NextRound) : BattleEvent(Seq);

public sealed record CrystalReadyEvent(long Seq, BattleSide Side) : BattleEvent(Seq);

public sealed record CrystalChosenEvent(long Seq, BattleSide Side, int Branch) : BattleEvent(Seq);

public sealed record PauseStateChangedEvent(long Seq, BattleSide Side, bool Paused, int RemainingSeconds) : BattleEvent(Seq);

public sealed record SurrenderEvent(long Seq, BattleSide Side) : BattleEvent(Seq);

public sealed record DisconnectedEvent(long Seq, BattleSide Side) : BattleEvent(Seq);

public sealed record BattleEndedEvent(long Seq, BattleSide? Winner, VictoryReason Reason) : BattleEvent(Seq);

/// <summary>胜负原因。</summary>
public enum VictoryReason
{
    /// <summary>对方英雄全部阵亡。</summary>
    Annihilation,

    /// <summary>回合耗尽判定（数量→血量百分比→具体生命→房主）。</summary>
    RoundExhausted,

    /// <summary>对方投降。</summary>
    Surrender,

    /// <summary>对方掉线（旧版 -99 判负）。</summary>
    Disconnect,
}
