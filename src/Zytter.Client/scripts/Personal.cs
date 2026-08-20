using System.Text.Json;
using Godot;
using Microsoft.AspNetCore.SignalR.Client;
using System.Threading.Tasks;

namespace Zytter.Client;

/// <summary>
/// 账号信息（复刻原版 Personal.java 的双栏布局）：
/// 左侧通行证信息（通行证/数字ID/总场次/胜场/败场/胜率），右侧账户管理
/// （修改用户名/密码、绑定e-mail）与 Elo/Rank/定级数据。
/// </summary>
public partial class Personal : Window
{
    private Label _passValue = null!;
    private Label _idValue = null!;
    private Label _totalValue = null!;
    private Label _winValue = null!;
    private Label _loseValue = null!;
    private Label _rateValue = null!;
    private Label _eloRow = null!;
    private Label _rankRow = null!;
    private Label _placeRow = null!;
    private Label _status = null!;
    private LineEdit _newUsername = null!;
    private LineEdit _oldPassword = null!;
    private LineEdit _newPassword = null!;
    private Button _changeUsername = null!;
    private Button _changePassword = null!;

    public override void _Ready()
    {
        _passValue = GetNode<Label>("%PassValue");
        _idValue = GetNode<Label>("%IdValue");
        _totalValue = GetNode<Label>("%TotalValue");
        _winValue = GetNode<Label>("%WinValue");
        _loseValue = GetNode<Label>("%LoseValue");
        _rateValue = GetNode<Label>("%RateValue");
        _eloRow = GetNode<Label>("%EloRow");
        _rankRow = GetNode<Label>("%RankRow");
        _placeRow = GetNode<Label>("%PlaceRow");
        _status = GetNode<Label>("%Status");
        _newUsername = GetNode<LineEdit>("%NewUsername");
        _oldPassword = GetNode<LineEdit>("%OldPassword");
        _newPassword = GetNode<LineEdit>("%NewPassword");
        _changeUsername = GetNode<Button>("%ChangeUsername");
        _changePassword = GetNode<Button>("%ChangePassword");

        GetNode<Button>("%Close").Pressed += () => EmitSignal(SignalName.CloseRequested);
        GetNode<Button>("%BindEmail").Pressed += () =>
            _status.Text = "绑定邮箱尚未实装（需要邮件基础设施），敬请期待。";

        _changeUsername.Pressed += () => _ = ChangeUsernameAsync();
        _changePassword.Pressed += () => _ = ChangePasswordAsync();

        _ = LoadInfoAsync();
    }

    private async Task LoadInfoAsync()
    {
        try
        {
            using var http = new System.Net.Http.HttpClient
            {
                BaseAddress = new Uri(Net.Instance.ServerUrl),
                Timeout = TimeSpan.FromSeconds(5),
            };
            var me = JsonSerializer.Deserialize<JsonElement>(
                await http.GetStringAsync($"/season/me?token={Uri.EscapeDataString(Net.Instance.Token)}"));

            int elo = me.GetProperty("elo").GetInt32();
            int best = me.GetProperty("bestElo").GetInt32();
            int placements = me.GetProperty("placementsLeft").GetInt32();
            int wins = me.GetProperty("wins").GetInt32();
            int losses = me.GetProperty("losses").GetInt32();
            int games = wins + losses;

            _passValue.Text = $"通行证：{me.GetProperty("username").GetString()}";
            _idValue.Text = $"数字ID：{me.GetProperty("id").GetInt64()}";
            _totalValue.Text = $"总场次：{games}";
            _winValue.Text = $"胜场：{wins}";
            _loseValue.Text = $"败场：{losses}";
            _rateValue.Text = $"胜率：{me.GetProperty("winRate").GetDouble()}%";

            _eloRow.Text = placements > 0 ? $"Elo：暂无（Best：{best}）" : $"Elo：{elo}（Best：{best}）";
            _rankRow.Text = placements > 0 ? "Rank：未定级" : $"Rank：{me.GetProperty("rank").GetString()}";
            _placeRow.Text = placements > 0 ? $"定级赛：还需胜利 {placements} 场才能激活" : "定级赛：已激活";
        }
        catch (Exception ex)
        {
            _status.Text = $"读取账号数据失败：{ex.Message}";
        }
    }

    private async Task ChangeUsernameAsync()
    {
        string name = _newUsername.Text.Trim();
        if (name.Length is < 2 or > 16 || name.Contains(' '))
        {
            _status.Text = "用户名需 2~16 个字符且不含空格。";
            return;
        }
        SetBusy(true, "正在修改用户名……");
        try
        {
            await Net.Instance.EnsureLobbyAsync();
            var result = await Net.Instance.Lobby!.InvokeAsync<AuthResult>("ChangeUsername", Net.Instance.Token, name);
            if (result.Success)
            {
                Net.Instance.Username = result.Username ?? name;
                Net.Instance.SaveSession();
                _status.Text = $"修改成功，新用户名：{Net.Instance.Username}";
                _newUsername.Text = "";
                await LoadInfoAsync();
            }
            else
            {
                _status.Text = $"修改失败：{result.Error}";
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

    private async Task ChangePasswordAsync()
    {
        string oldPw = _oldPassword.Text;
        string newPw = _newPassword.Text;
        if (oldPw.Length is < 6 or > 32 || newPw.Length is < 6 or > 32)
        {
            _status.Text = "密码需 6~32 个字符。";
            return;
        }
        SetBusy(true, "正在修改密码……");
        try
        {
            await Net.Instance.EnsureLobbyAsync();
            var result = await Net.Instance.Lobby!.InvokeAsync<AuthResult>("ChangePassword", Net.Instance.Token, oldPw, newPw);
            if (result.Success)
            {
                _status.Text = "密码修改成功。";
                _oldPassword.Text = "";
                _newPassword.Text = "";
            }
            else
            {
                _status.Text = $"修改失败：{result.Error}";
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
        _changeUsername.Disabled = busy;
        _changePassword.Disabled = busy;
        if (message is not null) _status.Text = message;
    }
}
