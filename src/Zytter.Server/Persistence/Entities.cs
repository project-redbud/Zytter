namespace Zytter.Server.Persistence;

/// <summary>玩家账户。密码使用 PBKDF2 加盐哈希（旧版明文存储，重构时修复）。</summary>
public sealed class Account
{
    public long Id { get; set; }
    public required string Username { get; set; }
    public required string PasswordHash { get; set; }
    public int Elo { get; set; } = 1200;
    public int RatingGames { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int BestElo { get; set; } = 1200;

    /// <summary>剩余定级赛场数（新账号 5；每赢一场 -1；归零后激活段位）。</summary>
    public int PlacementsLeft { get; set; } = 5;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>英雄养成状态（等级/经验等，MVP 阶段先占位）。</summary>
public sealed class HeroOwnership
{
    public required long AccountId { get; set; }
    public required int HeroId { get; set; }
    public int Level { get; set; } = 1;
    public int Experience { get; set; }
    public int Wins { get; set; }
    public int Plays { get; set; }
}

/// <summary>对局记录。</summary>
public sealed class MatchRecord
{
    public long Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long? WinnerAccountId { get; set; }
    public long? LoserAccountId { get; set; }
    public int Rounds { get; set; }
    public int WinnerKills { get; set; }
    public int LoserKills { get; set; }
    public required string Reason { get; set; } // 胜负原因（Annihilation/RoundExhausted/Surrender/Disconnect）
}
