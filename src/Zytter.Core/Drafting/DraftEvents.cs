using System.Text.Json.Serialization;

namespace Zytter.Core.Drafting;

/// <summary>
/// 禁选（B/P）流程事件流。服务器权威驱动，客户端只消费事件做展示。
/// 顺序严格串行：P1B1→P2B1→P1B2→P2B2 → P1P1→P2P1→P1P2→P2P2→P1P3→P2P3，
/// P1（房主，A 方）全程先手；被禁/已选英雄出池不可重复；超时弃权（BAN/PICK 写 0）。
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(DraftStartedEvent), "draft_started")]
[JsonDerivedType(typeof(DraftStepChangedEvent), "draft_step_changed")]
[JsonDerivedType(typeof(HeroBannedEvent), "hero_banned")]
[JsonDerivedType(typeof(HeroPickedEvent), "hero_picked")]
[JsonDerivedType(typeof(DraftOrderPhaseEvent), "draft_order_phase")]
[JsonDerivedType(typeof(DraftOrderedEvent), "draft_ordered")]
[JsonDerivedType(typeof(DraftCompletedEvent), "draft_completed")]
public abstract record DraftEvent;

/// <summary>禁选开始（12 人候选池全开）。</summary>
public sealed record DraftStartedEvent(int[] HeroPool) : DraftEvent;

/// <summary>当前轮到谁操作哪一步。Kind: ban/pick；StepIndex 从 0 起；TimeoutSeconds 为时限。</summary>
public sealed record DraftStepChangedEvent(int StepIndex, string Kind, string Side, int TimeoutSeconds) : DraftEvent;

/// <summary>禁用成功（HeroId=0 表示弃权）。</summary>
public sealed record HeroBannedEvent(string Side, int HeroId) : DraftEvent;

/// <summary>选用成功（HeroId=0 表示弃权）。</summary>
public sealed record HeroPickedEvent(string Side, int HeroId) : DraftEvent;

/// <summary>进入排序（决策）阶段：双方按出场顺序排列各自 PICK。TimeoutSeconds 为总决策时间。</summary>
public sealed record DraftOrderPhaseEvent(int TimeoutSeconds) : DraftEvent;

/// <summary>一方提交了出场顺序。</summary>
public sealed record DraftOrderedEvent(string Side, int[] Roster) : DraftEvent;

/// <summary>禁选完成：双方最终阵容确定，随后服务器创建权威对局。</summary>
public sealed record DraftCompletedEvent(int[] RosterA, int[] RosterB) : DraftEvent;
