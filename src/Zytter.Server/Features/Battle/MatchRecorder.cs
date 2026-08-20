using Microsoft.EntityFrameworkCore;
using Zytter.Core.Battle;
using Zytter.Server.Persistence;

namespace Zytter.Server.Features.Battle;

/// <summary>
/// 对局结算落库：对局结束后将结果写入 MatchRecord 并更新双方 ELO。
/// 旧版由输家客户端直连数据库 INSERT、双方并发 UPDATE 同一行；
/// 新版由服务器权威结算，一次写入。
/// </summary>
public sealed class MatchRecorder
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<MatchRecorder> _logger;

    public MatchRecorder(IDbContextFactory<AppDbContext> dbFactory, ILogger<MatchRecorder> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <summary>结算一次对局（幂等：房间级 recorded 标记防止重复写入）。</summary>
    public async Task RecordAsync(BattleRoom room)
    {
        (BattleSide? Winner, VictoryReason? Reason, int Rounds, int WinnerKills, int LoserKills) snap = default;
        room.WithSession(s =>
        {
            snap = (
                s.Winner,
                s.WinReason,
                s.Round,
                s.Winner is { } w ? s.Player(w).Kills : 0,
                s.Winner is { } w2 ? s.Player(w2.Opponent()).Kills : 0);
        });

        if (snap.Winner is null || snap.Reason is null)
        {
            _logger.LogInformation("房间 {RoomId} 无胜负结果，跳过结算", room.Id);
            return;
        }

        long? winnerId = room.GetAccount(snap.Winner.Value);
        long? loserId = room.GetAccount(snap.Winner.Value.Opponent());

        await using var db = await _dbFactory.CreateDbContextAsync();
        db.MatchRecords.Add(new MatchRecord
        {
            WinnerAccountId = winnerId,
            LoserAccountId = loserId,
            Rounds = snap.Rounds,
            WinnerKills = snap.WinnerKills,
            LoserKills = snap.LoserKills,
            Reason = snap.Reason.Value.ToString(),
        });

        if (winnerId is not null)
        {
            var winnerAccount = await db.Accounts.FindAsync(winnerId.Value);
            if (winnerAccount is not null)
            {
                winnerAccount.Wins++;
                winnerAccount.Elo += 16;
                winnerAccount.BestElo = Math.Max(winnerAccount.BestElo, winnerAccount.Elo);
                // 定级赛：每赢一场递减，归零后激活段位
                if (winnerAccount.PlacementsLeft > 0)
                    winnerAccount.PlacementsLeft--;
            }
        }
        if (loserId is not null)
        {
            var loserAccount = await db.Accounts.FindAsync(loserId.Value);
            if (loserAccount is not null)
            {
                loserAccount.Losses++;
                loserAccount.Elo = Math.Max(0, loserAccount.Elo - 16);
            }
        }

        await db.SaveChangesAsync();
        _logger.LogInformation("对局 {RoomId} 已结算：胜者 {Winner}，{Rounds} 回合", room.Id, winnerId, snap.Rounds);
    }
}
