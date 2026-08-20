using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Zytter.Server.IntegrationTests;

/// <summary>
/// 比赛确认流程测试：任一玩家放弃 → 双方都收到 MatchCancelled（整体取消，回到主界面）。
/// 前置条件：Zytter.Server 已运行在 127.0.0.1:17717。
/// </summary>
public class MatchConfirmTests
{
    private const string ServerUrl = "http://127.0.0.1:17717";

    private static async Task<HubConnection> ConnectAsync()
    {
        var connection = new HubConnectionBuilder()
            .WithUrl($"{ServerUrl}/hubs/lobby", options => options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets)
            .AddJsonProtocol(options =>
                options.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase)
            .Build();
        await connection.StartAsync();
        return connection;
    }

    [Fact]
    public async Task DeclineMatchCancelsBothSides()
    {
        var lobbyA = await ConnectAsync();
        var lobbyB = await ConnectAsync();
        await using var disposableA = lobbyA;
        await using var disposableB = lobbyB;

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var regA = await lobbyA.InvokeAsync<AuthResult>("Register", $"cA_{suffix}", "password123");
        var regB = await lobbyB.InvokeAsync<AuthResult>("Register", $"cB_{suffix}", "password123");
        Assert.True(regA.Success && regB.Success);

        var matchA = new TaskCompletionSource<MatchFoundDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var matchB = new TaskCompletionSource<MatchFoundDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        lobbyA.On<MatchFoundDto>("MatchFound", m => matchA.TrySetResult(m));
        lobbyB.On<MatchFoundDto>("MatchFound", m => matchB.TrySetResult(m));

        Assert.True(await lobbyA.InvokeAsync<bool>("EnqueueMatch", new EnqueueMatchRequest(regA.Token!, new[] { 1, 2, 3 })));
        Assert.True(await lobbyB.InvokeAsync<bool>("EnqueueMatch", new EnqueueMatchRequest(regB.Token!, new[] { 4, 5, 6 })));

        var foundA = await matchA.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var foundB = await matchB.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(foundA.RoomId, foundB.RoomId);

        // A 接受、B 放弃 → 双方都必须收到 MatchCancelled（A 不能困在"等待对方"里）
        var cancelledA = new TaskCompletionSource<MatchCancelledDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelledB = new TaskCompletionSource<MatchCancelledDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        lobbyA.On<MatchCancelledDto>("MatchCancelled", m =>
        {
            if (m.RoomId == foundA.RoomId) cancelledA.TrySetResult(m);
            return Task.CompletedTask;
        });
        lobbyB.On<MatchCancelledDto>("MatchCancelled", m =>
        {
            if (m.RoomId == foundB.RoomId) cancelledB.TrySetResult(m);
            return Task.CompletedTask;
        });

        Assert.True(await lobbyA.InvokeAsync<bool>("AcceptMatch", foundA.RoomId, regA.Token!));
        await lobbyB.InvokeAsync("DeclineMatch", foundB.RoomId, regB.Token!);

        var ca = await cancelledA.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var cb = await cancelledB.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(ca.RoomId, cb.RoomId);
        Assert.False(string.IsNullOrEmpty(ca.Reason));

        // 取消后双方可重新匹配（对局状态已清理）
        Assert.True(await lobbyA.InvokeAsync<bool>("EnqueueMatch", new EnqueueMatchRequest(regA.Token!, new[] { 1, 2, 3 })));
        Assert.True(await lobbyB.InvokeAsync<bool>("EnqueueMatch", new EnqueueMatchRequest(regB.Token!, new[] { 4, 5, 6 })));
        var reA = await matchA.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var reB = await matchB.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(reA.RoomId, reB.RoomId);
    }
}
