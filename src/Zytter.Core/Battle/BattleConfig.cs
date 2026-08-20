namespace Zytter.Core.Battle;

/// <summary>
/// 对战常量配置（全部数值来自 docs/01-combat-system.md 逆向规格）。
/// </summary>
public sealed class BattleConfig
{
    /// <summary>初始回合上限 35，可用回合延时道具 +5（每名玩家每局限 1 次）。</summary>
    public int InitialRoundLimit { get; init; } = 35;

    /// <summary>回合延时道具增加的回合数。</summary>
    public int RoundExtendAmount { get; init; } = 5;

    /// <summary>热身时间（秒）。</summary>
    public int WarmupSeconds { get; init; } = 20;

    /// <summary>励兵秣马（准备）阶段时长（秒）。</summary>
    public int PrepareSeconds { get; init; } = 3;

    /// <summary>准备阶段延长时长（任一方行动受限时，秒）。</summary>
    public int PrepareLimitedSeconds { get; init; } = 10;

    /// <summary>运筹帷幄（行动选择）阶段时长（秒）。</summary>
    public int ActionSeconds { get; init; } = 30;

    /// <summary>兵戎相见（结算展示）阶段时长（秒）。</summary>
    public int ResolveSeconds { get; init; } = 5;

    /// <summary>商店购物时限：普通商店回合（秒）。</summary>
    public int ShopSeconds { get; init; } = 20;

    /// <summary>商店购物时限：加时赛商店回合（秒）。</summary>
    public int ShopOvertimeSeconds { get; init; } = 15;

    /// <summary>前 5 个商店回合发放金币。</summary>
    public int ShopEarlyGold { get; init; } = 2;

    /// <summary>加时赛商店回合发放金币。</summary>
    public int ShopLateGold { get; init; } = 4;

    /// <summary>每回合结束发放金币。</summary>
    public int RoundEndGold { get; init; } = 1;

    /// <summary>击杀敌方英雄获得金币。</summary>
    public int KillGold { get; init; } = 3;

    /// <summary>商店回合编号（首 5 个）。</summary>
    public IReadOnlySet<int> ShopRounds { get; } = new HashSet<int> { 6, 13, 20, 27, 32 };

    /// <summary>加时赛商店周期：从 38 起每 5 回合一次。</summary>
    public int OvertimeShopStart { get; init; } = 38;

    /// <summary>每名玩家每场暂停次数。</summary>
    public int PauseTimes { get; init; } = 3;

    /// <summary>每次暂停时长（秒）。</summary>
    public int PauseSeconds { get; init; } = 60;

    /// <summary>允许投降的最早回合。</summary>
    public int SurrenderMinRound { get; init; } = 13;

    /// <summary>道具盒容量上限。</summary>
    public int ItemBoxCapacity { get; init; } = 30;

    /// <summary>掠夺机制：累计物理伤害每满 15 掠夺对方 1 金币（自己视角）。</summary>
    public int PlunderDamageStep { get; init; } = 15;

    /// <summary>低血保底阈值（最大生命百分比）。</summary>
    public double LowHpThreshold { get; init; } = 0.30;

    /// <summary>低血保底回复比例（最大生命百分比）。</summary>
    public double LowHpHealRatio { get; init; } = 0.07;

    /// <summary>判断是否为商店回合（含加时赛商店）。</summary>
    public bool IsShopRound(int nextRound) =>
        ShopRounds.Contains(nextRound) ||
        (nextRound >= OvertimeShopStart && (nextRound - OvertimeShopStart + 5) % 5 == 0);
}
