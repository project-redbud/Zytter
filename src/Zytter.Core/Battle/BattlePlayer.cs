using Zytter.Core.Data;
using Zytter.Core.Economy;

namespace Zytter.Core.Battle;

/// <summary>
/// 对局中的一名玩家（队伍）。持有跨英雄的持久状态：
/// 金币、道具盒、英雄名单与上场序号、限购标记、暂停次数、掠夺统计。
/// 当前上场英雄由 BattleSession 创建/切换并保存在 <see cref="Current"/>。
/// </summary>
public sealed class BattlePlayer
{
    public required BattleSide Side { get; init; }

    /// <summary>本场出战英雄名单（选人阶段确定，可排序，最多 3 名）。</summary>
    public required IReadOnlyList<HeroDefinition> Roster { get; init; }

    public Wallet Wallet { get; } = new();

    public ItemBox Box { get; } = new();

    /// <summary>当前上场英雄在名单中的下标。</summary>
    public int RosterIndex { get; set; }

    /// <summary>当前上场英雄；耗尽后为 null（该玩家战败）。</summary>
    public Combatant? Current { get; set; }

    /// <summary>是否还有后备英雄。</summary>
    public bool HasNextHero => RosterIndex + 1 < Roster.Count;

    /// <summary>下一名后备英雄（可能为 null）。</summary>
    public HeroDefinition? NextHero => HasNextHero ? Roster[RosterIndex + 1] : null;

    /// <summary>已死亡英雄数（对方击杀数）。</summary>
    public int Deaths { get; set; }

    /// <summary>击杀对方英雄数。</summary>
    public int Kills { get; set; }

    /// <summary>回合延时道具是否已购买（每局限购 1 次）。</summary>
    public bool RoundExtendPurchased { get; set; }

    /// <summary>回复药购买次数（每局限购 3 次，商店即时生效不入盒）。</summary>
    public int SmallPotionPurchased { get; set; }

    /// <summary>暂停剩余次数（每场 3 次）。</summary>
    public int PausesLeft { get; set; }

    /// <summary>暂停是否由本玩家发起且未解除。</summary>
    public bool PausedByMe { get; set; }

    /// <summary>夜宴之声已用次数（满 3 次自动脱下）。</summary>
    public int NightBanquetUses { get; set; }

    /// <summary>加时赛"吃装备"是否已使用（全场一次）。</summary>
    public bool HasEatenEquipment { get; set; }

    /// <summary>累计造成的物理伤害（掠夺机制：每满 15 掠夺对方 1 金币）。</summary>
    public double PhysicalDamageDealt { get; set; }

    /// <summary>累计承受的物理伤害。</summary>
    public double PhysicalDamageTaken { get; set; }

    /// <summary>回合末魔回封锁标记（月光剑副作用：本回合无法魔法回复）。</summary>
    public bool MpRegenBlocked { get; set; }
}
