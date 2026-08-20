using Godot;
using Microsoft.AspNetCore.SignalR.Client;
using System.Threading.Tasks;

namespace Zytter.Client;

/// <summary>
/// 登录界面（原版 Login）：服务器地址 + 凭据 + 注册入口。
/// 已有会话时直接进入大厅（与"记住登录"一致）；<c>--bot</c> 无头机器人模式自动注册并匹配。
/// </summary>
public partial class Login : Control
{
    private LineEdit _serverUrl = null!;
    private LineEdit _username = null!;
    private LineEdit _password = null!;
    private Button _enterGame = null!;
    private Button _registerLink = null!;
    private Button _blogLink = null!;
    private Button _quitLink = null!;
    private Button _copyrightLink = null!;
    private Label _status = null!;

    public override void _Ready()
    {
        _serverUrl = GetNode<LineEdit>("%ServerUrl");
        _username = GetNode<LineEdit>("%Username");
        _password = GetNode<LineEdit>("%Password");
        _enterGame = GetNode<Button>("%EnterGame");
        _registerLink = GetNode<Button>("%RegisterLink");
        _blogLink = GetNode<Button>("%BlogLink");
        _quitLink = GetNode<Button>("%QuitLink");
        _copyrightLink = GetNode<Button>("%CopyrightLink");
        _status = GetNode<Label>("%Status");

        _enterGame.Pressed += OnLoginPressed;
        _registerLink.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/Register.tscn");
        _blogLink.Pressed += () => OS.ShellOpen("https://www.wrss.org/zytter");
        _quitLink.Pressed += () => GetTree().Quit();
        _copyrightLink.Pressed += () => OS.ShellOpen("https://hzxl.wiki/zytter");

        _serverUrl.Text = Net.Instance.ServerUrl;
        _username.Text = Net.Instance.Username;

        // 机器人模式：--bot --botname=xxx 自动注册并匹配（验证用，不进大厅 UI）
        // 优先于会话恢复，避免残留会话导致机器人不匹配
        var args = OS.GetCmdlineUserArgs();
        if (args.Contains("--bot"))
        {
            Net.Instance.IsBot = true;
            string botName = args.FirstOrDefault(a => a.StartsWith("--botname="))?["--botname=".Length..]
                             ?? $"bot_{Guid.NewGuid():N}"[..12];
            _username.Text = botName;
            _password.Text = "botpassword";
            _ = BotLoopAsync();
            return;
        }

        // 已有会话：先向服务器验证令牌有效性（修复"自动登录 401"：服务器重启后旧令牌失效，
        // 此时取消自动登录并停留本界面，而不是带着失效会话进入主界面到处 401）
        if (!string.IsNullOrEmpty(Net.Instance.Token))
        {
            _ = ValidateSessionAsync();
            return;
        }
    }

    /// <summary>自动登录前验证会话令牌并登记在线；失效/重复登录/无法连接则清除会话并停留登录界面。</summary>
    private async Task ValidateSessionAsync()
    {
        _status.Text = "正在验证登录状态……";
        try
        {
            using var http = new System.Net.Http.HttpClient
            {
                BaseAddress = new Uri(Net.Instance.ServerUrl),
                Timeout = TimeSpan.FromSeconds(5),
            };
            using var resp = await http.GetAsync($"/season/me?token={Uri.EscapeDataString(Net.Instance.Token)}");
            if (!resp.IsSuccessStatusCode)
            {
                ClearSession("登录状态已失效，请重新登录。");
                return;
            }

            // 重复登录检测：同账号已被其他客户端占用 → 取消自动登录，停留本界面
            try
            {
                await Net.Instance.EnsureLobbyAsync();
                bool claimed = await Net.Instance.Lobby!.InvokeAsync<bool>("ClaimOnline", Net.Instance.Token);
                if (!claimed)
                {
                    ClearSession("该账号已在其他客户端登录，请勿重复登录。");
                    return;
                }
            }
            catch
            {
                ClearSession("无法连接服务器，请检查服务器地址。");
                return;
            }

            GetTree().ChangeSceneToFile("res://scenes/MainMenu.tscn");
        }
        catch
        {
            ClearSession("无法连接服务器，请检查服务器地址。");
        }
    }

    private void ClearSession(string message)
    {
        Net.Instance.Token = "";
        Net.Instance.Username = "";
        Net.Instance.SaveSession();
        _status.Text = message;
    }

    /// <summary>无头机器人：注册 → 匹配 → 接受比赛（双方确认）→ 自动进入禁选场景。</summary>
    private async Task BotLoopAsync()
    {
        try
        {
            GD.Print($"[bot] 启动，用户名 {_username.Text}");
            await Net.Instance.EnsureLobbyAsync();
            var result = await Net.Instance.Lobby!.InvokeAsync<AuthResult>("Register", _username.Text.Trim(), _password.Text);
            if (!result.Success)
            {
                result = await Net.Instance.Lobby!.InvokeAsync<AuthResult>("Login", _username.Text.Trim(), _password.Text);
            }
            Net.Instance.Token = result.Token!;
            Net.Instance.Username = result.Username ?? _username.Text;
            GD.Print($"[bot] 登录成功，开始匹配");

            // 处理程序须在 AcceptMatch 之前注册（服务器可能立即回 MatchConfirmed，避免竞态丢失）
            var subs = new List<IDisposable>();
            try
            {
                for (int attempt = 1; attempt <= 5; attempt++)
                {
                    foreach (var s in subs) s.Dispose();
                    subs.Clear();

                    var matchTcs = new TaskCompletionSource<MatchFoundDto>(TaskCreationOptions.RunContinuationsAsynchronously);
                    var confirmTcs = new TaskCompletionSource<MatchConfirmedDto>(TaskCreationOptions.RunContinuationsAsynchronously);
                    var cancelTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                    subs.Add(Net.Instance.Lobby!.On<MatchFoundDto>("MatchFound", m =>
                    {
                        GD.Print($"[bot] 匹配成功 room={m.RoomId} side={m.Side}");
                        matchTcs.TrySetResult(m);
                        return Task.CompletedTask;
                    }));
                    subs.Add(Net.Instance.Lobby!.On<MatchConfirmedDto>("MatchConfirmed", m =>
                    {
                        GD.Print($"[bot] 双方已确认比赛 room={m.RoomId}");
                        confirmTcs.TrySetResult(m);
                        return Task.CompletedTask;
                    }));
                    subs.Add(Net.Instance.Lobby!.On<MatchCancelledDto>("MatchCancelled", m =>
                    {
                        GD.Print($"[bot] 比赛取消：{m.Reason}");
                        cancelTcs.TrySetResult(true);
                        return Task.CompletedTask;
                    }));

                    await Net.Instance.Lobby!.InvokeAsync("EnqueueMatch", new EnqueueMatchRequest(Net.Instance.Token, new[] { 1, 2, 3 }));
                    var found = await matchTcs.Task.WaitAsync(TimeSpan.FromSeconds(120));

                    // 接受比赛：处理程序已注册，双方确认会立即送达
                    await Net.Instance.Lobby!.InvokeAsync("AcceptMatch", found.RoomId, Net.Instance.Token);

                    var done = await Task.WhenAny(
                        confirmTcs.Task,
                        cancelTcs.Task,
                        Task.Delay(TimeSpan.FromSeconds(20)));
                    if (done == confirmTcs.Task && confirmTcs.Task.IsCompletedSuccessfully
                        && confirmTcs.Task.Result.RoomId == found.RoomId)
                    {
                        Net.Instance.RoomId = found.RoomId;
                        Net.Instance.Side = found.Side;
                        GetTree().ChangeSceneToFile("res://scenes/Draft.tscn");
                        return;
                    }
                    GD.Print($"[bot] 确认未达成（第 {attempt} 次），重新匹配");
                }
            }
            finally
            {
                foreach (var s in subs) s.Dispose();
            }
            GD.PrintErr("[bot] 多次匹配均未确认，退出");
            GetTree().Quit(1);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[bot] 失败：{ex}");
            GetTree().Quit(1);
        }
    }

    private async void OnLoginPressed()
    {
        SetBusy(true, "正在登录……");
        try
        {
            Net.Instance.ServerUrl = _serverUrl.Text.Trim();
            await Net.Instance.EnsureLobbyAsync();
            var result = await Net.Instance.Lobby!.InvokeAsync<AuthResult>("Login", _username.Text.Trim(), _password.Text);
            if (result.Success)
            {
                Net.Instance.Token = result.Token!;
                Net.Instance.Username = result.Username ?? _username.Text.Trim();
                Net.Instance.SaveSession();
                GetTree().ChangeSceneToFile("res://scenes/MainMenu.tscn");
            }
            else
            {
                _status.Text = $"登录失败：{result.Error}";
            }
        }
        catch (Exception ex)
        {
            _status.Text = $"连接服务器失败：{ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy, string? message = null)
    {
        _enterGame.Disabled = busy;
        _registerLink.Disabled = busy;
        if (message is not null) _status.Text = message;
    }
}
