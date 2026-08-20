using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Zytter.Core.Battle;
using Zytter.Core.Data;
using Zytter.Core.Drafting;
using Zytter.Server.Features.Accounts;
using Zytter.Server.Features.Battle;
using Zytter.Server.Features.Drafting;
using Zytter.Server.Features.Matchmaking;

namespace Zytter.Server.Hubs;

/// <summary>
/// 大厅 Hub：账户操作 + 匹配队列 + 禁选（B/P）交互。
/// </summary>
public sealed class LobbyHub : Hub
{
    private readonly AccountService _accounts;
    private readonly Matchmaker _matchmaker;
    private readonly MatchConfirmRegistry _confirmRegistry;
    private readonly MatchConfirmHost _confirmHost;
    private readonly DraftRegistry _drafts;
    private readonly DraftLoopHost _draftLoop;
    private readonly ILogger<LobbyHub> _logger;

    /// <summary>账户 → 连接（用于匹配成功定向推送）。</summary>
    public static readonly ConcurrentDictionary<long, string> Connections = new();

    public LobbyHub(
        AccountService accounts,
        Matchmaker matchmaker,
        MatchConfirmRegistry confirmRegistry,
        MatchConfirmHost confirmHost,
        DraftRegistry drafts,
        DraftLoopHost draftLoop,
        ILogger<LobbyHub> logger)
    {
        _accounts = accounts;
        _matchmaker = matchmaker;
        _confirmRegistry = confirmRegistry;
        _confirmHost = confirmHost;
        _drafts = drafts;
        _draftLoop = draftLoop;
        _logger = logger;
    }

    public async Task<AccountService.AuthResult> Register(string username, string password) =>
        await _accounts.RegisterAsync(username, password);

    /// <summary>登录：若该账号已在线（其他连接占用）则拒绝，防止两个客户端共用同一账号。</summary>
    public async Task<AccountService.AuthResult> Login(string username, string password)
    {
        var result = await _accounts.LoginAsync(username, password);
        if (result.Success && result.AccountId is { } id
            && Connections.TryGetValue(id, out var existing) && existing != Context.ConnectionId)
        {
            return new AccountService.AuthResult(false, "该账号已在其他客户端登录");
        }
        return result;
    }

    /// <summary>
    /// 登记"当前连接在线"（自动登录/进入主界面时调用）。
    /// 若该账号已被其他连接占用则返回 false（重复登录检测）。
    /// </summary>
    public async Task<bool> ClaimOnline(string token)
    {
        var account = await _accounts.GetByTokenAsync(token)
            ?? throw new HubException("无效的登录凭证");
        if (Connections.TryGetValue(account.Id, out var existing) && existing != Context.ConnectionId)
            return false;
        Connections[account.Id] = Context.ConnectionId;
        return true;
    }

    /// <summary>登出：释放在线占用（退出登录时调用，允许同账号后续重新登录）。</summary>
    public async Task Logout(string token)
    {
        var account = await _accounts.GetByTokenAsync(token);
        if (account is null) return;
        Connections.TryRemove(account.Id, out _);
        _matchmaker.Cancel(account.Id);
        await Task.CompletedTask;
    }

    /// <summary>修改用户名（账号信息界面）。</summary>
    public async Task<AccountService.AuthResult> ChangeUsername(string token, string newUsername) =>
        await _accounts.ChangeUsernameAsync(token, newUsername);

    /// <summary>修改密码（账号信息界面）。</summary>
    public async Task<AccountService.AuthResult> ChangePassword(string token, string oldPassword, string newPassword) =>
        await _accounts.ChangePasswordAsync(token, oldPassword, newPassword);

    /// <summary>加入匹配队列（roster 为本方 3 名英雄 ID）。</summary>
    public async Task<bool> EnqueueMatch(EnqueueMatchRequest request)
    {
        var account = await _accounts.GetByTokenAsync(request.Token)
            ?? throw new HubException("无效的登录凭证");

        if (request.Roster is null || request.Roster.Length is < 1 or > 3)
            throw new HubException("英雄名单必须为 1~3 名英雄");
        var catalog = GameDataCatalog.LoadDefault();
        foreach (var heroId in request.Roster)
        {
            if (!catalog.Heroes.ContainsKey(heroId))
                throw new HubException($"英雄 #{heroId} 不存在");
        }

        Connections[account.Id] = Context.ConnectionId;
        _matchmaker.Enqueue(new MatchQueueEntry(account.Id, request.Roster));
        await TryMatchAsync();
        return true;
    }

    public async Task CancelMatch(string token)
    {
        var account = await _accounts.GetByTokenAsync(token);
        if (account is null) return;
        _matchmaker.Cancel(account.Id);
        await Task.CompletedTask;
    }

    // ==================== 比赛确认（复刻原版接受比赛流程） ====================

    /// <summary>接受比赛：双方都接受后才创建禁选房间并推送 MatchConfirmed。</summary>
    public async Task<bool> AcceptMatch(Guid roomId, string token)
    {
        var account = await _accounts.GetByTokenAsync(token)
            ?? throw new HubException("无效的登录凭证");
        var pending = _confirmRegistry.Get(roomId);
        if (pending is null || pending.Finished) return false;

        if (account.Id == pending.AId) pending.AAccepted = true;
        else if (account.Id == pending.BId) pending.BAccepted = true;
        else return false;

        if (pending.AAccepted && pending.BAccepted)
        {
            pending.Finished = true;
            await StartDraftAsync(pending);
            _confirmRegistry.Remove(roomId);
        }
        return true;
    }

    /// <summary>放弃比赛：任一放弃 → 整体取消，双方都返回主界面。</summary>
    public async Task DeclineMatch(Guid roomId, string token)
    {
        var account = await _accounts.GetByTokenAsync(token)
            ?? throw new HubException("无效的登录凭证");
        var pending = _confirmRegistry.Get(roomId);
        if (pending is null || pending.Finished) return;

        pending.Finished = true;
        _logger.LogInformation("比赛确认被放弃：{RoomId}（{A} vs {B}）", roomId, pending.AName, pending.BName);
        await _confirmHost.CancelPendingAsync(pending, "对方未接受比赛，比赛已取消");
        _confirmRegistry.Remove(roomId);
    }

    /// <summary>双方确认后：创建禁选房间并通知双方进入 B/P。</summary>
    private async Task StartDraftAsync(PendingMatchState pending)
    {
        var roomId = pending.RoomId;
        var draft = _drafts.Create(roomId, GameDataCatalog.LoadDefault().Heroes.Keys.ToList());
        _draftLoop.Register(roomId, pending.AId, pending.BId);
        draft.Start();
        _logger.LogInformation("双方已确认比赛：{RoomId}（{A} vs {B}），进入禁选", roomId, pending.AName, pending.BName);

        var confirmed = new MatchConfirmedDto(roomId);
        foreach (var accountId in new[] { pending.AId, pending.BId })
        {
            if (Connections.TryGetValue(accountId, out var connectionId))
                await Clients.Client(connectionId).SendAsync("MatchConfirmed", confirmed);
        }
    }

    /// <summary>
    /// 加入单人练习：立即创建人机对局（玩家恒为 A 方/房主·先手，AI 为 B 方）。
    /// 返回匹配结果，客户端直接进入禁选；AI 由服务器侧 AiDriver 驱动。
    /// </summary>
    public async Task<MatchFoundDto> EnqueueAiMatch(EnqueueMatchRequest request)
    {
        var account = await _accounts.GetByTokenAsync(request.Token)
            ?? throw new HubException("无效的登录凭证");

        if (request.Roster is null || request.Roster.Length is < 1 or > 3)
            throw new HubException("英雄名单必须为 1~3 名英雄");
        var catalog = GameDataCatalog.LoadDefault();
        foreach (var heroId in request.Roster)
        {
            if (!catalog.Heroes.ContainsKey(heroId))
                throw new HubException($"英雄 #{heroId} 不存在");
        }

        Connections[account.Id] = Context.ConnectionId;

        var roomId = Guid.NewGuid();
        var draft = _drafts.Create(roomId, catalog.Heroes.Keys.ToList());
        _draftLoop.Register(roomId, account.Id, BattleRoom.AiAccountId);
        draft.Start();
        _logger.LogInformation("人机对战已创建：玩家 {Account} vs AI，房间 {RoomId}", account.Id, roomId);

        // AI 的真实阵容由禁选阶段决定，此处占位为空
        return new MatchFoundDto(roomId, BattleSide.A.ToString(), "电脑", BattleRoom.AiAccountId, Array.Empty<int>());
    }

    /// <summary>撮合：凑齐两人 → 登记待确认比赛（双方接受后才创建禁选房间）→ 双向推送。</summary>
    private async Task TryMatchAsync()
    {
        while (_matchmaker.TryMatch() is { } pair)
        {
            var (a, b) = pair;
            _logger.LogInformation("匹配成功：{A} vs {B}，队列剩余 {Queue}", a.AccountId, b.AccountId, _matchmaker.QueueLength);

            var roomId = Guid.NewGuid();
            string nameA = await GetUsername(a.AccountId);
            string nameB = await GetUsername(b.AccountId);
            _confirmRegistry.Add(roomId, a.AccountId, b.AccountId, nameA, nameB);

            var resultA = new MatchFoundDto(roomId, BattleSide.A.ToString(), nameB, b.AccountId, b.Roster.ToArray());
            var resultB = new MatchFoundDto(roomId, BattleSide.B.ToString(), nameA, a.AccountId, a.Roster.ToArray());

            bool sentA = Connections.TryGetValue(a.AccountId, out var connA);
            bool sentB = Connections.TryGetValue(b.AccountId, out var connB);
            if (sentA)
                await Clients.Client(connA!).SendAsync("MatchFound", resultA);
            if (sentB)
                await Clients.Client(connB!).SendAsync("MatchFound", resultB);
            _logger.LogInformation("MatchFound 已推送 A={SentA} B={SentB}（待确认房间 {RoomId}）", sentA, sentB, roomId);
        }
    }

    // ==================== 禁选（B/P） ====================

    /// <summary>加入禁选房间：校验归属 → 入组 → 补发快照与历史事件。</summary>
    public async Task<DraftSnapshotDto> DraftJoin(Guid roomId, string token)
    {
        var account = await _accounts.GetByTokenAsync(token)
            ?? throw new HubException("无效的登录凭证");
        var draft = _drafts.Get(roomId)
            ?? throw new HubException("禁选不存在或已结束");
        var side = GetDraftSide(roomId, account.Id);

        await Groups.AddToGroupAsync(Context.ConnectionId, DraftLoopHost.GroupName(roomId));

        var snapshot = new DraftSnapshotDto(
            roomId, side, draft.Phase.ToString(), draft.StepIndex, draft.StepRemainingSeconds,
            draft.HeroPool.ToArray(), draft.BansA.ToArray(), draft.BansB.ToArray(),
            draft.PicksA.ToArray(), draft.PicksB.ToArray());

        // 补发历史事件（客户端重建完整禁选状态）
        foreach (var e in draft.DrainEvents())
            await Clients.Caller.SendAsync("DraftEvents", new[] { e });

        return snapshot;
    }

    private string GetDraftSide(Guid roomId, long accountId)
    {
        // 通过 DraftLoopHost 的归属表判定
        var owner = ResolveDraftOwner(roomId);
        if (owner.AccountA == accountId) return "A";
        if (owner.AccountB == accountId) return "B";
        throw new HubException("你不属于该禁选");
    }

    private (long AccountA, long AccountB) ResolveDraftOwner(Guid roomId) =>
        _draftLoop.GetOwner(roomId) ?? throw new HubException("禁选归属信息缺失");

    /// <summary>禁用英雄。</summary>
    public async Task DraftBan(Guid roomId, string token, int heroId)
    {
        var (draft, side) = await ResolveDraftAsync(roomId, token);
        draft.Ban(side, heroId);
        await Task.CompletedTask;
    }

    /// <summary>选用英雄。</summary>
    public async Task DraftPick(Guid roomId, string token, int heroId)
    {
        var (draft, side) = await ResolveDraftAsync(roomId, token);
        draft.Pick(side, heroId);
        await Task.CompletedTask;
    }

    /// <summary>提交出场顺序。</summary>
    public async Task DraftOrder(Guid roomId, string token, int[] order)
    {
        var (draft, side) = await ResolveDraftAsync(roomId, token);
        draft.SubmitOrder(side, order);
        await Task.CompletedTask;
    }

    private async Task<(DraftSession Draft, string Side)> ResolveDraftAsync(Guid roomId, string token)
    {
        var account = await _accounts.GetByTokenAsync(token)
            ?? throw new HubException("无效的登录凭证");
        var draft = _drafts.Get(roomId)
            ?? throw new HubException("禁选不存在或已结束");
        var side = GetDraftSide(roomId, account.Id);
        return (draft, side);
    }

    private async Task<string> GetUsername(long accountId)
    {
        // 简化：通过 token 不可行，直接以 ID 兜底；MVP 阶段显示 ID
        await Task.CompletedTask;
        return $"玩家{accountId}";
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var entry = Connections.FirstOrDefault(kv => kv.Value == Context.ConnectionId);
        if (entry.Key != 0)
        {
            Connections.TryRemove(entry.Key, out _);
            _matchmaker.Cancel(entry.Key);
        }
        await base.OnDisconnectedAsync(exception);
    }
}
