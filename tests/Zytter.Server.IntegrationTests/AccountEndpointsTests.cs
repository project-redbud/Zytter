using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Zytter.Server.IntegrationTests;

/// <summary>
/// 账号信息接口测试：修改用户名 / 修改密码（前置条件：Zytter.Server 已运行在 127.0.0.1:17717）。
/// </summary>
public class AccountEndpointsTests
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
    public async Task ChangeUsernameAndPassword()
    {
        var lobby = await ConnectAsync();
        await using var disposable = lobby;

        var suffix = Guid.NewGuid().ToString("N")[..8];
        string user = $"acc_{suffix}";
        var reg = await lobby.InvokeAsync<AuthResult>("Register", user, "password123");
        Assert.True(reg.Success, reg.Error);
        Assert.NotNull(reg.Token);

        // 占用名应失败（另一个账号已注册该用户名）
        string otherName = $"accX_{suffix}";
        Assert.True((await lobby.InvokeAsync<AuthResult>("Register", otherName, "password123")).Success);
        var taken = await lobby.InvokeAsync<AuthResult>("ChangeUsername", reg.Token!, otherName);
        Assert.False(taken.Success);
        Assert.Contains("占用", taken.Error);

        // 改回自己当前用户名视为成功（幂等）
        var sameName = await lobby.InvokeAsync<AuthResult>("ChangeUsername", reg.Token!, user);
        Assert.True(sameName.Success);

        string newName = $"acc2_{suffix}";
        var changed = await lobby.InvokeAsync<AuthResult>("ChangeUsername", reg.Token!, newName);
        Assert.True(changed.Success, changed.Error);
        Assert.Equal(newName, changed.Username);

        // 用新用户名重新登录验证持久化
        var relogin = await lobby.InvokeAsync<AuthResult>("Login", newName, "password123");
        Assert.True(relogin.Success);

        // 修改密码：原密码错误应失败，正确应成功
        var wrongOld = await lobby.InvokeAsync<AuthResult>("ChangePassword", relogin.Token!, "wrong-old", "newpass456");
        Assert.False(wrongOld.Success);
        Assert.Contains("原密码", wrongOld.Error);

        var ok = await lobby.InvokeAsync<AuthResult>("ChangePassword", relogin.Token!, "password123", "newpass456");
        Assert.True(ok.Success, ok.Error);

        // 新密码可登录，旧密码不可
        Assert.True((await lobby.InvokeAsync<AuthResult>("Login", newName, "newpass456")).Success);
        Assert.False((await lobby.InvokeAsync<AuthResult>("Login", newName, "password123")).Success);
    }

    [Fact]
    public async Task DuplicateLoginIsRejected()
    {
        var lobby1 = await ConnectAsync();
        var lobby2 = await ConnectAsync();
        await using var disposable1 = lobby1;
        await using var disposable2 = lobby2;

        var suffix = Guid.NewGuid().ToString("N")[..8];
        string user = $"dup_{suffix}";
        var reg = await lobby1.InvokeAsync<AuthResult>("Register", user, "password123");
        Assert.True(reg.Success);

        // 客户端 1 登记在线
        Assert.True(await lobby1.InvokeAsync<bool>("ClaimOnline", reg.Token!));

        // 客户端 2 复用同一会话 → 重复登录被拒绝（ClaimOnline 返回 false）
        Assert.False(await lobby2.InvokeAsync<bool>("ClaimOnline", reg.Token!));

        // 客户端 2 手动登录同账号 → 登录被拒绝
        var relogin = await lobby2.InvokeAsync<AuthResult>("Login", user, "password123");
        Assert.False(relogin.Success);
        Assert.Contains("其他客户端", relogin.Error);

        // 客户端 1 登出 → 释放占用 → 客户端 2 可登记
        await lobby1.InvokeAsync("Logout", reg.Token!);
        Assert.True(await lobby2.InvokeAsync<bool>("ClaimOnline", reg.Token!));
    }
}
