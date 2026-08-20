using Microsoft.AspNetCore.SignalR;
using Zytter.Server.Features.Accounts;
using Zytter.Server.Features.Battle;

namespace Zytter.Server.Hubs;

/// <summary>对局聊天消息（点对点广播给同房间双方）。</summary>
public sealed record ChatMessageDto(string Sender, string Text);

/// <summary>
/// 聊天 Hub：对局内聊天（对应旧版 17723 聊天端口，无频道/无历史的点对点转发）。
/// </summary>
public sealed class ChatHub : Hub
{
    private readonly AccountService _accounts;
    private readonly BattleRegistry _battles;

    public ChatHub(AccountService accounts, BattleRegistry battles)
    {
        _accounts = accounts;
        _battles = battles;
    }

    /// <summary>加入对局聊天组。</summary>
    public async Task JoinChat(Guid roomId, string token)
    {
        var account = await _accounts.GetByTokenAsync(token)
            ?? throw new HubException("无效的登录凭证");
        var room = _battles.Get(roomId)
            ?? throw new HubException("对局不存在或已结束");
        if (room.GetSide(account.Id) is null)
            throw new HubException("你不属于该对局");

        await Groups.AddToGroupAsync(Context.ConnectionId, BattleLoopHost.GroupName(roomId));
    }

    /// <summary>发送聊天消息，广播给房间内双方。</summary>
    public async Task SendChat(Guid roomId, string token, string text)
    {
        var account = await _accounts.GetByTokenAsync(token)
            ?? throw new HubException("无效的登录凭证");
        var room = _battles.Get(roomId)
            ?? throw new HubException("对局不存在或已结束");
        if (room.GetSide(account.Id) is null)
            throw new HubException("你不属于该对局");

        if (string.IsNullOrWhiteSpace(text) || text.Length > 200)
            throw new HubException("消息长度需为 1~200 字符");

        await Clients.Group(BattleLoopHost.GroupName(roomId))
            .SendAsync("ChatMessage", new ChatMessageDto(account.Username, text.Trim()));
    }
}
