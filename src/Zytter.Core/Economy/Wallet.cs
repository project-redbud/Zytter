using Zytter.Core.Common;

namespace Zytter.Core.Economy;

/// <summary>
/// 对局内金币钱包。所有变动必须经过校验（余额不足抛规则违规），
/// 旧版金币直接 int 加减散落各处，无法审计。
/// </summary>
public sealed class Wallet
{
    public int Gold { get; private set; }

    public Wallet(int initial = 0)
    {
        Gold = initial;
    }

    public void Add(int amount)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        Gold += amount;
    }

    /// <summary>尝试消费金币；余额不足抛出规则违规（reason=insufficient_gold）。</summary>
    public void Spend(int amount)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        if (Gold < amount)
            throw new RuleViolationException("insufficient_gold", $"金币不足：需要 {amount}，当前 {Gold}");
        Gold -= amount;
    }

    public bool CanAfford(int amount) => Gold >= amount;
}
