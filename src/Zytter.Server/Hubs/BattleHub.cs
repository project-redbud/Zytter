using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Zytter.Core.Battle;
using Zytter.Core.Data;
using Zytter.Server.Features.Accounts;
using Zytter.Server.Features.Battle;

namespace Zytter.Server.Hubs;

/// <summary>
/// 对局 Hub：权威战斗交互。所有方法校验登录令牌与房间归属，
/// 命令在房间门锁内执行（与引擎 Tick 串行），事件由 BattleLoopHost 广播。
/// </summary>
public sealed class BattleHub : Hub
{
    private readonly AccountService _accounts;
    private readonly BattleRegistry _battles;

    /// <summary>连接 → 账户映射（Hub 实例按调用创建，状态必须放静态表）。</summary>
    private static readonly ConcurrentDictionary<string, long> ConnectionAccounts = new();

    public BattleHub(AccountService accounts, BattleRegistry battles)
    {
        _accounts = accounts;
        _battles = battles;
    }

    /// <summary>加入对局房间：校验归属 → 入组 → 补发完整快照与事件日志。</summary>
    public async Task<BattleSnapshotDto> JoinBattle(JoinBattleRequest request)
    {
        var account = await _accounts.GetByTokenAsync(request.Token)
            ?? throw new HubException("无效的登录凭证");
        var room = _battles.Get(request.RoomId)
            ?? throw new HubException("对局不存在或已结束");
        var side = room.GetSide(account.Id)
            ?? throw new HubException("你不属于该对局");

        await Groups.AddToGroupAsync(Context.ConnectionId, BattleLoopHost.GroupName(room.Id));
        ConnectionAccounts[Context.ConnectionId] = account.Id;

        var session = room.Session;
        var mySide = side;
        var enemySide = side.Opponent();
        var my = session.Player(mySide).Current;
        var enemy = session.Player(enemySide).Current;
        var snapshot = new BattleSnapshotDto(
            room.Id, side.ToString(), session.Round, session.RoundLimit,
            session.Phase.ToString(), session.PhaseRemainingSeconds,
            session.PlayerA.Roster.Select(h => h.Name).ToArray(),
            session.PlayerB.Roster.Select(h => h.Name).ToArray(),
            session.PlayerA.Roster.Select(h => h.Id).ToArray(),
            session.PlayerB.Roster.Select(h => h.Id).ToArray(),
            my?.Hero.Name ?? "", my?.Hero.Id ?? 0, my?.Stats.Hp ?? 0, my?.Stats.MaxHp ?? 0, my?.Stats.Mp ?? 0, my?.Stats.MaxMp ?? 0,
            enemy?.Hero.Name ?? "", enemy?.Hero.Id ?? 0, enemy?.Stats.Hp ?? 0, enemy?.Stats.MaxHp ?? 0, enemy?.Stats.Mp ?? 0, enemy?.Stats.MaxMp ?? 0,
            session.Log.Count > 0 ? session.Log[^1].Seq : 0);

        // 补发完整事件日志（客户端从零重建状态）
        foreach (var e in session.Log)
            await Clients.Caller.SendAsync("Events", new[] { e });

        return snapshot;
    }

    private BattleRoom GetRoom(long accountId)
    {
        // 简化：按连接上下文缓存房间归属
        return _battles.All.FirstOrDefault(r => r.GetSide(accountId) is not null)
            ?? throw new HubException("你不在任何对局中");
    }

    private BattleSide GetSide(BattleRoom room, long accountId) =>
        room.GetSide(accountId) ?? throw new HubException("你不属于该对局");

    /// <summary>行动阶段提交行动。</summary>
    public async Task SubmitAction(string token, ActionDto dto)
    {
        var (room, side) = await ResolveAsync(token);
        var action = ToPlayerAction(dto);
        room.WithSession(s => s.Execute(new SubmitActionCommand(side, action)));
        await Task.CompletedTask;
    }

    /// <summary>商店购买。</summary>
    public async Task BuyItem(string token, int itemId)
    {
        var (room, side) = await ResolveAsync(token);
        room.WithSession(s => s.Execute(new ShopPurchaseCommand(side, itemId)));
        await Task.CompletedTask;
    }

    /// <summary>穿戴/脱下装备（itemId=0 脱下）。</summary>
    public async Task Equip(string token, string slot, int itemId)
    {
        var (room, side) = await ResolveAsync(token);
        var equipmentSlot = slot switch
        {
            "Z" => EquipmentSlot.Z,
            "X" => EquipmentSlot.X,
            _ => throw new HubException("无效的装备槽"),
        };
        room.WithSession(s => s.Execute(new EquipCommand(side, equipmentSlot, itemId)));
        await Task.CompletedTask;
    }

    /// <summary>选择结晶之力分支（1/2/3）。</summary>
    public async Task ChooseCrystal(string token, int branch)
    {
        var (room, side) = await ResolveAsync(token);
        room.WithSession(s => s.Execute(new CrystalChoiceCommand(side, branch)));
        await Task.CompletedTask;
    }

    /// <summary>准备阶段复苏选择（0=复苏胶囊 1=高级复苏胶囊 2=取消）。</summary>
    public async Task ReviveChoice(string token, int choice)
    {
        var (room, side) = await ResolveAsync(token);
        var reviveChoice = choice switch
        {
            0 => Core.Battle.ReviveChoice.UseRevive,
            1 => Core.Battle.ReviveChoice.UseRevivePlus,
            _ => Core.Battle.ReviveChoice.Cancel,
        };
        room.WithSession(s => s.Execute(new ReviveChoiceCommand(side, reviveChoice)));
        await Task.CompletedTask;
    }

    /// <summary>暂停/解除暂停。</summary>
    public async Task Pause(string token, bool resume)
    {
        var (room, side) = await ResolveAsync(token);
        room.WithSession(s => s.Execute(new PauseCommand(side, resume)));
        await Task.CompletedTask;
    }

    /// <summary>投降。</summary>
    public async Task Surrender(string token)
    {
        var (room, side) = await ResolveAsync(token);
        room.WithSession(s => s.Execute(new SurrenderCommand(side)));
        await Task.CompletedTask;
    }

    private async Task<(BattleRoom Room, BattleSide Side)> ResolveAsync(string token)
    {
        var account = await _accounts.GetByTokenAsync(token)
            ?? throw new HubException("无效的登录凭证");
        var room = GetRoom(account.Id);
        return (room, GetSide(room, account.Id));
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // 对局中掉线 → 对方判胜（对应旧版 -99 语义）
        if (ConnectionAccounts.TryRemove(Context.ConnectionId, out long accountId))
        {
            foreach (var room in _battles.All)
            {
                if (room.GetSide(accountId) is { } side)
                    room.WithSession(s => s.Disconnect(side));
            }
        }
        await base.OnDisconnectedAsync(exception);
    }

    private static PlayerAction ToPlayerAction(ActionDto dto) => dto.Kind switch
    {
        "skill" => new CastSkillAction(dto.Slot switch
        {
            "Q" => SkillSlot.Q,
            "W" => SkillSlot.W,
            "E" => SkillSlot.E,
            "R" => SkillSlot.R,
            _ => throw new HubException("无效的技能槽"),
        }, dto.ChainQ),
        "attack" => new BasicAttackAction(),
        "item" => new UseItemAction(dto.ItemId ?? throw new HubException("缺少物品 ID")),
        "skip" => new SkipAction(),
        _ => throw new HubException($"未知行动类型 {dto.Kind}"),
    };
}
