namespace Zytter.Client;

// 与服务端 Hubs/Dtos.cs 的 JSON 形状一致的客户端 DTO（camelCase 由协议约定）

public sealed record AuthResult(bool Success, string? Error = null, string? Token = null, long? AccountId = null, string? Username = null);

public sealed record EnqueueMatchRequest(string Token, int[] Roster);

public sealed record MatchFoundDto(Guid RoomId, string Side, string OpponentName, long OpponentId, int[] Roster);

/// <summary>双方均接受比赛 → 进入禁选。</summary>
public sealed record MatchConfirmedDto(Guid RoomId);

/// <summary>任一放弃/超时 → 整体取消比赛，双方返回主界面。</summary>
public sealed record MatchCancelledDto(Guid RoomId, string Reason);

public sealed record JoinBattleRequest(Guid RoomId, string Token);

public sealed record BattleSnapshotDto(
    Guid RoomId, string Side, int Round, int RoundLimit, string Phase,
    double PhaseRemainingSeconds, string[] TeamA, string[] TeamB, int[] RosterA, int[] RosterB,
    string MyHeroName, int MyHeroId, int MyHp, int MyMaxHp, int MyMp, int MyMaxMp,
    string EnemyHeroName, int EnemyHeroId, int EnemyHp, int EnemyMaxHp, int EnemyMp, int EnemyMaxMp,
    long LastSeq);

public sealed record ActionDto(string Kind, string? Slot = null, int? ItemId = null, bool ChainQ = false);

/// <summary>聊天消息（ChatHub 广播）。</summary>
public sealed record ChatMessageDto(string Sender, string Text);
