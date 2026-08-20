using Godot;
using Microsoft.AspNetCore.SignalR.Client;
using System.Threading.Tasks;

namespace Zytter.Client;

/// <summary>
/// 注册界面（原版 Reg）：用户名(2~16) + 密码(6~32) + 确认。
/// 规则在客户端与服务器双重校验；成功即保存会话并进入大厅。
/// </summary>
public partial class Register : Control
{
    private LineEdit _serverUrl = null!;
    private LineEdit _username = null!;
    private LineEdit _password = null!;
    private LineEdit _confirm = null!;
    private Button _register = null!;
    private Button _back = null!;
    private Label _status = null!;

    public override void _Ready()
    {
        _serverUrl = GetNode<LineEdit>("%ServerUrl");
        _username = GetNode<LineEdit>("%Username");
        _password = GetNode<LineEdit>("%Password");
        _confirm = GetNode<LineEdit>("%Confirm");
        _register = GetNode<Button>("%Register");
        _back = GetNode<Button>("%Back");
        _status = GetNode<Label>("%Status");

        _register.Pressed += OnRegisterPressed;
        _back.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/Login.tscn");
        _serverUrl.Text = Net.Instance.ServerUrl;
    }

    private async void OnRegisterPressed()
    {
        var user = _username.Text.Trim();
        var pw = _password.Text;

        if (user.Length is < 2 or > 16 || user.Contains(' '))
        {
            _status.Text = "用户名需 2~16 字符且不含空格。";
            return;
        }
        if (pw.Length is < 6 or > 32)
        {
            _status.Text = "密码需 6~32 字符。";
            return;
        }
        if (pw != _confirm.Text)
        {
            _status.Text = "两次输入的密码不一致。";
            return;
        }

        SetBusy(true, "正在注册……");
        try
        {
            Net.Instance.ServerUrl = _serverUrl.Text.Trim();
            await Net.Instance.EnsureLobbyAsync();
            var result = await Net.Instance.Lobby!.InvokeAsync<AuthResult>("Register", user, pw);
            if (result.Success)
            {
                Net.Instance.Token = result.Token!;
                Net.Instance.Username = result.Username ?? user;
                Net.Instance.SaveSession();
                _status.Text = $"注册成功，欢迎 {Net.Instance.Username}！";
                GetTree().ChangeSceneToFile("res://scenes/MainMenu.tscn");
            }
            else
            {
                _status.Text = $"注册失败：{result.Error}";
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
        _register.Disabled = busy;
        _back.Disabled = busy;
        if (message is not null) _status.Text = message;
    }
}
