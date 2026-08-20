namespace Zytter.Core.Buffs;

/// <summary>
/// Buff 运行时实例：定义 + 层数 + 剩余回合 + 数值参数 + 专属状态。
/// RemainingRounds == -1 表示无回合概念（永久型或按层数管理，如解放真名/汐之抉择）。
/// </summary>
public sealed class BuffInstance
{
    public Data.BuffDefinition Definition { get; }

    /// <summary>层数（可叠加的 Buff 以此计数）。</summary>
    public int Stacks { get; set; }

    /// <summary>剩余持续回合；-1 = 永久/按层管理；0 = 到期待移除。</summary>
    public int RemainingRounds { get; set; }

    /// <summary>数值参数（如冰之羽翼累计抵挡伤害、神谕指定规则）。</summary>
    public double V1 { get; set; }

    public double V2 { get; set; }

    /// <summary>
    /// 逐层到期队列（解放真名：每层独立 10 回合后扣回）。
    /// 元素为 rend 值，由 BattleSession 在结算末弹出到期层数。
    /// </summary>
    public List<int> StackExpiryRends { get; } = new();

    public BuffInstance(Data.BuffDefinition definition, int stacks, int remainingRounds)
    {
        Definition = definition;
        Stacks = stacks;
        RemainingRounds = remainingRounds;
    }

    public bool IsExpired => RemainingRounds == 0;

    public bool IsPermanent => RemainingRounds < 0;
}
