namespace Zytter.Core.Common;

/// <summary>
/// 战斗引擎使用的随机数抽象。
/// 旧版直接 new Random()，无法复现；新引擎要求所有随机数经此接口获取，
/// 注入固定种子即可确定性重放整局对战（测试、回放、断线重连的基础）。
/// </summary>
public interface IRng
{
    /// <summary>返回 [0, maxExclusive) 的均匀随机整数。</summary>
    int Next(int maxExclusive);

    /// <summary>返回 [minInclusive, maxExclusive) 的均匀随机整数。</summary>
    int Next(int minInclusive, int maxExclusive);

    /// <summary>返回 [0, 1) 的均匀随机浮点数。</summary>
    double NextDouble();

    /// <summary>以给定概率返回 true。</summary>
    bool Chance(double probability);
}

/// <summary>
/// xorshift64* 确定性随机数发生器。同一种子在任何平台/版本产生同一序列，
/// 用于单元测试与对局重放。
/// </summary>
public sealed class SeededRng : IRng
{
    private ulong _state;

    public SeededRng(ulong seed)
    {
        _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
    }

    public uint NextUInt32()
    {
        _state += 0x9E3779B97F4A7C15UL;
        ulong z = _state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return (uint)(z ^ (z >> 31));
    }

    public int Next(int maxExclusive)
    {
        if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
        return (int)(NextUInt32() % (uint)maxExclusive);
    }

    public int Next(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), "maxExclusive 必须大于 minInclusive。");
        return minInclusive + Next(maxExclusive - minInclusive);
    }

    public double NextDouble() => NextUInt32() * (1.0 / 4294967296.0);

    public bool Chance(double probability) => NextDouble() < probability;
}

/// <summary>生产环境使用的随机数发生器（基于 System.Random，线程不安全，禁止跨线程共享）。</summary>
public sealed class SystemRng : IRng
{
    private readonly Random _random = new();

    public int Next(int maxExclusive) => _random.Next(maxExclusive);

    public int Next(int minInclusive, int maxExclusive) => _random.Next(minInclusive, maxExclusive);

    public double NextDouble() => _random.NextDouble();

    public bool Chance(double probability) => _random.NextDouble() < probability;
}
