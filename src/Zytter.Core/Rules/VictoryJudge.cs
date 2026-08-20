namespace Zytter.Core.Rules;

/// <summary>
/// 胜负判定（docs/01-combat-system.md §7）。
/// 修复旧版缺陷：百分比比较改用浮点除法（旧版 int/int 整数除法会误判平手）。
/// </summary>
public static class VictoryJudge
{
    /// <summary>
    /// 回合耗尽（r==limitr）且无人英雄耗尽时的判定链：
    /// 1. 剩余英雄数量多者胜；2. 当前英雄生命百分比高者胜；
    /// 3. 百分比相同 → 具体生命值高者胜；4. 仍相同 → 房主（A 方）胜。
    /// </summary>
    public static Battle.BattleSide JudgeByRoundExhaustion(
        Battle.BattlePlayer playerA, Battle.BattlePlayer playerB)
    {
        int remainingA = playerA.Roster.Count - playerA.RosterIndex;
        int remainingB = playerB.Roster.Count - playerB.RosterIndex;

        if (remainingA != remainingB)
            return remainingA > remainingB ? Battle.BattleSide.A : Battle.BattleSide.B;

        var heroA = playerA.Current;
        var heroB = playerB.Current;

        // 双方当前英雄同时存活时按血量判定；单侧为 null（异常状态）时按数量已处理
        double percentA = heroA is null ? 0 : (double)heroA.Stats.Hp / heroA.Stats.MaxHp;
        double percentB = heroB is null ? 0 : (double)heroB.Stats.Hp / heroB.Stats.MaxHp;

        if (Math.Abs(percentA - percentB) > 1e-9)
            return percentA > percentB ? Battle.BattleSide.A : Battle.BattleSide.B;

        int hpA = heroA?.Stats.Hp ?? 0;
        int hpB = heroB?.Stats.Hp ?? 0;

        if (hpA != hpB)
            return hpA > hpB ? Battle.BattleSide.A : Battle.BattleSide.B;

        // 全部平手 → 房主（A 方）胜
        return Battle.BattleSide.A;
    }
}
