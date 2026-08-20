namespace Zytter.Core.Battle;

/// <summary>
/// 对战双方。A 方即房间创建者（房主）：
/// 行动力相同 → 房主先手；回合耗尽全部平手 → 房主胜（原版规则）。
/// </summary>
public enum BattleSide
{
    A,
    B,
}

public static class BattleSideExtensions
{
    public static BattleSide Opponent(this BattleSide side) =>
        side == BattleSide.A ? BattleSide.B : BattleSide.A;
}
