namespace Zytter.Server.Hubs;

/// <summary>客户端 → 服务器的命令与请求 DTO（强类型，替代旧版 int 操作码协议）。</summary>

/// <summary>行动阶段提交的行动。</summary>
public sealed record ActionDto(string Kind, string? Slot = null, int? ItemId = null, bool ChainQ = false);

/// <summary>加入对局请求。</summary>
public sealed record JoinBattleRequest(Guid RoomId, string Token);

/// <summary>匹配入队请求。</summary>
public sealed record EnqueueMatchRequest(string Token, int[] Roster);

/// <summary>匹配成功通知（含对方 ID，供客户端展示"对手ID"）。</summary>
public sealed record MatchFoundDto(Guid RoomId, string Side, string OpponentName, long OpponentId, int[] Roster);

/// <summary>双方均接受比赛 → 通知进入禁选。</summary>
public sealed record MatchConfirmedDto(Guid RoomId);

/// <summary>任一放弃/超时 → 整体取消比赛通知（双方都回到主界面）。</summary>
public sealed record MatchCancelledDto(Guid RoomId, string Reason);

/// <summary>对局快照（加入房间时补发完整状态，含双方当前英雄的真实数值）。</summary>
public sealed record BattleSnapshotDto(
    Guid RoomId,
    string Side,
    int Round,
    int RoundLimit,
    string Phase,
    double PhaseRemainingSeconds,
    string[] TeamA,
    string[] TeamB,
    int[] RosterA,
    int[] RosterB,
    string MyHeroName,
    int MyHeroId,
    int MyHp,
    int MyMaxHp,
    int MyMp,
    int MyMaxMp,
    string EnemyHeroName,
    int EnemyHeroId,
    int EnemyHp,
    int EnemyMaxHp,
    int EnemyMp,
    int EnemyMaxMp,
    long LastSeq);

/// <summary>禁选快照（加入禁选房间时补发当前状态）。</summary>
public sealed record DraftSnapshotDto(
    Guid RoomId,
    string Side,
    string Phase,
    int StepIndex,
    double StepRemainingSeconds,
    int[] HeroPool,
    int[] BansA,
    int[] BansB,
    int[] PicksA,
    int[] PicksB);
