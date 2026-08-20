using Godot;

namespace Zytter.Client;

/// <summary>
/// 欢迎/开始界面（复刻原版 Welcome）：全屏 welcome.jpg 背景 + 右下角版权文本，
/// 3 秒后自动进入登录界面；点击任意处（SkipArea）可立即跳过。
/// 本界面不播放 BGM（仅主界面 MainMenu 播放大厅 BGM）。
/// </summary>
public partial class Welcome : Control
{
    private float _elapsed;
    private bool _done;

    public override void _Ready()
    {
        GetNode<Button>("%SkipArea").Pressed += GoLogin;
    }

    public override void _Process(double delta)
    {
        if (_done) return;
        _elapsed += (float)delta;
        if (_elapsed >= 3.0f) GoLogin();
    }

    private void GoLogin()
    {
        if (_done) return;
        _done = true;
        GetTree().ChangeSceneToFile("res://scenes/Login.tscn");
    }
}
