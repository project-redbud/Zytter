namespace Zytter.Server.Features.Season;

/// <summary>
/// 赛季段位体系（docs/03 §7.2）：
/// 定级赛（PlacementsLeft &gt; 0）期间为"未定级"；
/// 激活后按 Elo 阈值：≥2600=S，≥2200=A+，≥1800=A，≥1400=B，≥1000=C，≥600=D，否则 E。
/// 权重（getRank）：未定级=0，E=1，D=2，C=3，B=4，A=5，A+=6，S=7（Best 只升不降用）。
/// </summary>
public static class SeasonCalculator
{
    public const int PlacementGames = 5;

    public static bool IsPlaced(int placementsLeft) => placementsLeft <= 0;

    public static string RankFor(int elo, int placementsLeft)
    {
        if (!IsPlaced(placementsLeft)) return "未定级";
        return elo switch
        {
            >= 2600 => "S",
            >= 2200 => "A+",
            >= 1800 => "A",
            >= 1400 => "B",
            >= 1000 => "C",
            >= 600 => "D",
            _ => "E",
        };
    }

    /// <summary>段位权重（Best 记录比较用）。</summary>
    public static int RankWeight(string rank) => rank switch
    {
        "S" => 7,
        "A+" => 6,
        "A" => 5,
        "B" => 4,
        "C" => 3,
        "D" => 2,
        "E" => 1,
        _ => 0,
    };
}
