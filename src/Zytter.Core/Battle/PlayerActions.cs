using Zytter.Core.Data;

namespace Zytter.Core.Battle;

/// <summary>
/// 玩家在行动阶段提交的行动（意图）。旧版操作码为 int（1=Q…6+=道具，-1=放弃），
/// 新版为强类型记录，服务器权威校验。
/// </summary>
public abstract record PlayerAction
{
    /// <summary>先手特权层级（对应旧版 xdl += 9999 / += 999 的"无视行动力"实现）。</summary>
    public virtual int PriorityTier => 0;
}

/// <summary>释放技能。ChainQ 仅对杨圣诺 W（星辰陨落）有效：确认后追加一次 Q。</summary>
public sealed record CastSkillAction(SkillSlot Slot, bool ChainQ = false) : PlayerAction;

/// <summary>普通攻击。</summary>
public sealed record BasicAttackAction : PlayerAction;

/// <summary>使用消耗品（id 3~12）。</summary>
public sealed record UseItemAction(int ItemId) : PlayerAction;

/// <summary>放弃行动（超时自动提交）。</summary>
public sealed record SkipAction : PlayerAction;

/// <summary>结算时的实际动作（附先手特权），由引擎包装。</summary>
public sealed record ResolvedAction(PlayerAction Action, int PriorityTier, BattleSide Side);
