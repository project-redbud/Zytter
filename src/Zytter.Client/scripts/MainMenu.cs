using System.Text.Json;
using Godot;
using Microsoft.AspNetCore.SignalR.Client;
using System.Threading.Tasks;

namespace Zytter.Client;

/// <summary>
/// 主界面（复刻原版 Main.java / Match.java）：main.jpg 背景 + 图标按钮 + 玩家数据 + 匹配流程。
/// 匹配成功后弹出【比赛确认框】：显示"对手ID"与 15 秒倒计时；接受后等待对方接受，
/// 双方都确认才进入 B/P；任一放弃/超时 → 服务器向双方推送 MatchCancelled，整体取消并恢复主界面。
/// </summary>
public partial class MainMenu : Control
{
    private Label _loadingTip = null!;
    private Label _serverStatus = null!;
    private Label _username = null!;
    private Label _eloValue = null!;
    private Label _rankValue = null!;
    private Label _placeValue = null!;
    private Label _status = null!;
    private Label _matchingLabel = null!;
    private Label _matchingTimer = null!;

    private Button _match = null!;
    private Button _aiMatch = null!;
    private Button _cancel = null!;
    private Button _exit = null!;
    private Button _heroes = null!;
    private Button _rankList = null!;
    private Button _blog = null!;
    private Button _personal = null!;
    private Button _logout = null!;

    // 比赛确认弹窗（复刻原版 Match.java 接受比赛流程）
    private Control _matchConfirmOverlay = null!;
    private Label _confirmInfo = null!;
    private Label _confirmCountdown = null!;
    private Button _acceptBtn = null!;
    private Button _declineBtn = null!;
    private TaskCompletionSource<bool>? _acceptTcs;      // 用户接受/放弃决策
    private double _acceptRemaining;                     // 接受倒计时

    /// <summary>当前已弹出的模态窗（赛季排行/图鉴/账号信息，避免重复打开）。</summary>
    private Window? _modalWindow;

    private bool _matching;
    private double _matchSeconds;
    private TaskCompletionSource<MatchFoundDto>? _matchTcs;
    private long _accountId;
    private string _usernameText = "";

    public override void _Ready()
    {
        _loadingTip = GetNode<Label>("%LoadingTip");
        _serverStatus = GetNode<Label>("%ServerStatus");
        _username = GetNode<Label>("%Username");
        _eloValue = GetNode<Label>("%EloValue");
        _rankValue = GetNode<Label>("%RankValue");
        _placeValue = GetNode<Label>("%PlaceValue");
        _status = GetNode<Label>("%Status");
        _matchingLabel = GetNode<Label>("%MatchingLabel");
        _matchingTimer = GetNode<Label>("%MatchingTimer");

        _match = GetNode<Button>("%MatchBtn");
        _aiMatch = GetNode<Button>("%AiMatchBtn");
        _cancel = GetNode<Button>("%CancelBtn");
        _exit = GetNode<Button>("%ExitBtn");
        _heroes = GetNode<Button>("%HeroesBtn");
        _rankList = GetNode<Button>("%RankListBtn");
        _blog = GetNode<Button>("%BlogBtn");
        _personal = GetNode<Button>("%PersonalBtn");
        _logout = GetNode<Button>("%LogoutBtn");

        _matchConfirmOverlay = GetNode<Control>("%MatchConfirmOverlay");
        _confirmInfo = GetNode<Label>("%ConfirmInfo");
        _confirmCountdown = GetNode<Label>("%ConfirmCountdown");
        _acceptBtn = GetNode<Button>("%AcceptBtn");
        _declineBtn = GetNode<Button>("%DeclineBtn");

        _match.Pressed += OnMatchPressed;
        _aiMatch.Pressed += OnAiMatchPressed;
        _cancel.Pressed += () => _ = CancelMatchAsync();
        _exit.Pressed += OnExitPressed;
        _heroes.Pressed += () => OpenModal("res://scenes/HeroesList.tscn", "英雄 & 物品图鉴");
        _personal.Pressed += () => OpenModal("res://scenes/Personal.tscn", "账号信息");
        _rankList.Pressed += () => OpenModal("res://scenes/Season.tscn", "赛季数据");
        _blog.Pressed += OnBlogPressed;
        _logout.Pressed += OnLogoutPressed;

        _acceptBtn.Pressed += () => _acceptTcs?.TrySetResult(true);
        _declineBtn.Pressed += () => _acceptTcs?.TrySetResult(false);

        // 未登录（会话失效或手动返回）：退回登录
        if (string.IsNullOrEmpty(Net.Instance.Token))
        {
            GetTree().ChangeSceneToFile("res://scenes/Login.tscn");
            return;
        }

        _usernameText = Net.Instance.Username;
        Net.Instance.PlayLobbyBgm();
        SetButtonsEnabled(false);
        _ = InitAsync();
    }

    public override void _Process(double delta)
    {
        if (_matching)
        {
            _matchSeconds += delta;
            int minutes = (int)_matchSeconds / 60;
            int seconds = (int)_matchSeconds % 60;
            _matchingTimer.Text = $"已匹配时长：{minutes}分{seconds}秒";
        }

        // 接受比赛倒计时（仅等待用户决策期间）
        if (_acceptTcs is not null)
        {
            _acceptRemaining -= delta;
            if (_acceptRemaining <= 0)
            {
                _acceptRemaining = 0;
                _acceptTcs.TrySetResult(false);
            }
            else
            {
                _confirmCountdown.Text = $"接受比赛倒计时：{(int)Math.Ceiling(_acceptRemaining)} 秒";
            }
        }

        // 安全网：匹配流程标记进行中但确认弹窗/匹配 UI 都已隐藏 → 说明流程已结束而未恢复，强制恢复
        if (_matching && !_matchConfirmOverlay.Visible && !_matchingLabel.Visible && !_matchingTimer.Visible)
        {
            _matching = false;
            RestoreMainMenu();
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
            OnExitPressed();
    }

    // ==================== 初始化（复刻原版 Init 线程的载入提示） ====================

    private async Task InitAsync()
    {
        // 重复登录防御：若该账号已被其他客户端占用，清除会话并退回登录界面
        try
        {
            await Net.Instance.EnsureLobbyAsync();
            if (!await Net.Instance.Lobby!.InvokeAsync<bool>("ClaimOnline", Net.Instance.Token))
            {
                Net.Instance.Token = "";
                Net.Instance.Username = "";
                Net.Instance.SaveSession();
                GetTree().ChangeSceneToFile("res://scenes/Login.tscn");
                return;
            }
        }
        catch
        {
            // 大厅连接暂不可用：继续走 HTTP 初始化（匹配时会再次建立连接）
        }

        foreach (var text in new[] { "正在开始载入英雄数据......", "正在开始载入技能数据......", "正在开始载入物品数据......" })
        {
            _loadingTip.Text = text;
            await Task.Delay(250);
        }

        try
        {
            using var http = new System.Net.Http.HttpClient
            {
                BaseAddress = new Uri(Net.Instance.ServerUrl),
                Timeout = TimeSpan.FromSeconds(5),
            };

            try
            {
                var info = JsonSerializer.Deserialize<JsonElement>(await http.GetStringAsync("/info"));
                _serverStatus.Text = $"[{info.GetProperty("version").GetString()}] {info.GetProperty("name").GetString()}";
            }
            catch
            {
                _serverStatus.Text = $"[{Net.Instance.ServerUrl}] 服务器信息加载失败";
            }

            try
            {
                var me = JsonSerializer.Deserialize<JsonElement>(
                    await http.GetStringAsync($"/season/me?token={Uri.EscapeDataString(Net.Instance.Token)}"));
                _accountId = me.GetProperty("id").GetInt64();
                _usernameText = me.GetProperty("username").GetString() ?? _usernameText;
                _username.Text = _usernameText;
                _eloValue.Text = me.GetProperty("elo").GetInt32().ToString();
                int placements = me.GetProperty("placementsLeft").GetInt32();
                _rankValue.Text = placements > 0 ? "未定级" : me.GetProperty("rank").GetString()!;
                _placeValue.Text = placements > 0 ? $"{placements}/5" : "已定级";
            }
            catch (Exception ex)
            {
                _username.Text = _usernameText;
                _status.Text = $"读取账号数据失败：{ex.Message}";
            }
        }
        finally
        {
            _loadingTip.Visible = false;
            SetButtonsEnabled(true);
        }
    }

    // ==================== 匹配（复刻原版 Main.java / Match.java 交互） ====================

    private async void OnMatchPressed()
    {
        if (_matching)
        {
            _ = CancelMatchAsync();
            return;
        }

        _matching = true;
        _matchSeconds = 0;
        _match.Visible = false;
        _cancel.Visible = false;
        _exit.Disabled = true;
        SetButtonsEnabled(false);
        _matchingLabel.Text = "正在连接至匹配服务器...";
        _matchingLabel.Visible = true;
        _matchingTimer.Visible = false;

        bool matched = false;
        IDisposable? sub = null;
        try
        {
            await Task.Delay(1000); // 复刻原版：先停留 1 秒显示连接文案
            if (!_matching) return;

            Net.Instance.PlaySfx("startmatch");
            _matchingLabel.Text = "正在寻找比赛...";
            _matchingTimer.Visible = true;
            _cancel.Visible = true;

            await Net.Instance.EnsureLobbyAsync();

            _matchTcs = new TaskCompletionSource<MatchFoundDto>(TaskCreationOptions.RunContinuationsAsynchronously);
            sub = Net.Instance.Lobby!.On<MatchFoundDto>("MatchFound", m =>
            {
                _matchTcs.TrySetResult(m);
                return Task.CompletedTask;
            });

            await Net.Instance.Lobby!.InvokeAsync("EnqueueMatch", new EnqueueMatchRequest(Net.Instance.Token, new[] { 1, 2, 3 }));

            var found = await _matchTcs.Task.WaitAsync(TimeSpan.FromSeconds(300)); // 原版最长匹配 5 分钟
            if (!_matching) return;

            // 比赛确认：双方都接受才进入禁选
            bool accepted = await ShowAcceptFlowAsync(found);
            if (!accepted)
            {
                _status.Text = "比赛已取消。";
                return;
            }

            matched = true;
            Net.Instance.RoomId = found.RoomId;
            Net.Instance.Side = found.Side;
            Net.Instance.PlaySfx("startmatch"); // 原版：确认后"正在连接至游戏服务器"音效
            _status.Text = $"比赛开始！你是 {found.Side} 方，对手：{found.OpponentName}，进入禁选……";
            GetTree().ChangeSceneToFile("res://scenes/Draft.tscn");
        }
        catch (OperationCanceledException)
        {
            _status.Text = "已取消匹配。";
        }
        catch (TimeoutException)
        {
            _status.Text = "匹配超时（5 分钟），请重试。";
        }
        catch (Exception ex)
        {
            _status.Text = $"匹配失败：{ex.Message}";
        }
        finally
        {
            sub?.Dispose();
            _matchTcs = null;
            _acceptTcs = null;
            _matching = false;
            _matchConfirmOverlay.Visible = false;
            _matchingLabel.Visible = false;
            _matchingTimer.Visible = false;
            _cancel.Visible = false;

            // 立即恢复主界面可用（防"取消比赛后按钮全部不可用"）；进入禁选则跳过
            if (!matched && IsInsideTree())
                RestoreMainMenu();
        }
    }

    /// <summary>
    /// 比赛确认流程：弹窗显示"对手ID"与倒计时 → 接受后发送 AcceptMatch 等待对方；
    /// 双方确认（MatchConfirmed）返回 true；放弃/超时（DeclineMatch）或对方放弃
    /// （MatchCancelled）返回 false。弹窗出现时播放 gamematchisready 并保持大厅 BGM。
    /// </summary>
    private async Task<bool> ShowAcceptFlowAsync(MatchFoundDto found)
    {
        var confirmTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        IDisposable? okSub = null;
        IDisposable? cancelSub = null;
        try
        {
            okSub = Net.Instance.Lobby!.On<MatchConfirmedDto>("MatchConfirmed", m =>
            {
                if (m.RoomId == found.RoomId) confirmTcs.TrySetResult(true);
                return Task.CompletedTask;
            });
            cancelSub = Net.Instance.Lobby!.On<MatchCancelledDto>("MatchCancelled", m =>
            {
                if (m.RoomId == found.RoomId)
                {
                    _status.Text = m.Reason;
                    confirmTcs.TrySetResult(false);
                }
                return Task.CompletedTask;
            });

            // 弹窗出现：显示对手、播放就绪音效并确保大厅 BGM 在播
            _confirmInfo.Text = $"对手：{found.OpponentName}\n你是 {found.Side} 方";
            _acceptBtn.Disabled = false;
            _declineBtn.Disabled = false;
            _matchConfirmOverlay.Visible = true;
            Net.Instance.PlaySfx("gamematchisready");
            Net.Instance.PlayLobbyBgm();

            // 等待用户决策（接受/放弃/15 秒超时）
            _acceptTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _acceptRemaining = 15;
            _confirmCountdown.Text = "接受比赛倒计时：15 秒";
            bool decided = await _acceptTcs.Task;
            _acceptTcs = null;

            if (!decided)
            {
                // 放弃或超时：通知服务器整体取消
                await DeclineMatchAsync(found.RoomId);
                return false;
            }

            // 已接受：向服务器登记接受（此前缺失，导致人机对战永远等不到双方确认）
            try
            {
                await Net.Instance.EnsureLobbyAsync();
                await Net.Instance.Lobby!.InvokeAsync("AcceptMatch", found.RoomId, Net.Instance.Token);
            }
            catch
            {
                // 接受登记失败：服务器 15 秒超时兜底会取消本场
            }

            // 等待对方接受
            _confirmCountdown.Text = "等待对方接受比赛...";
            _acceptBtn.Disabled = true;
            _declineBtn.Disabled = true;
            return await confirmTcs.Task;
        }
        finally
        {
            okSub?.Dispose();
            cancelSub?.Dispose();
            _matchConfirmOverlay.Visible = false;
        }
    }

    private async Task DeclineMatchAsync(Guid roomId)
    {
        try
        {
            await Net.Instance.EnsureLobbyAsync();
            await Net.Instance.Lobby!.InvokeAsync("DeclineMatch", roomId, Net.Instance.Token);
        }
        catch
        {
            // 通知失败不阻塞本地复位（服务器侧 15 秒超时兜底）
        }
    }

    private async Task CancelMatchAsync()
    {
        _matchTcs?.TrySetCanceled();
        try
        {
            await Net.Instance.EnsureLobbyAsync();
            await Net.Instance.Lobby!.InvokeAsync("CancelMatch", Net.Instance.Token);
        }
        catch
        {
            // 取消失败不阻塞本地状态复位（由 OnMatchPressed 的 finally 恢复 UI）
        }
    }

    /// <summary>恢复主界面为可操作状态（防"主界面无法动弹"）。</summary>
    private void RestoreMainMenu()
    {
        _match.Visible = true;
        _matchConfirmOverlay.Visible = false;
        _matchingLabel.Visible = false;
        _matchingTimer.Visible = false;
        _cancel.Visible = false;
        _exit.Disabled = false;
        SetButtonsEnabled(true);
    }

    // ==================== 人机练习（重制版新增） ====================

    private async void OnAiMatchPressed()
    {
        SetButtonsEnabled(false);
        _status.Text = "正在进入单人练习……";
        try
        {
            await Net.Instance.EnsureLobbyAsync();
            var found = await Net.Instance.Lobby!.InvokeAsync<MatchFoundDto>(
                "EnqueueAiMatch", new EnqueueMatchRequest(Net.Instance.Token, new[] { 1, 2, 3 }));

            Net.Instance.RoomId = found.RoomId;
            Net.Instance.Side = found.Side;
            Net.Instance.PlaySfx("gamematchisready");
            _status.Text = $"已进入单人练习，对手：{found.OpponentName}，进入禁选……";
            GetTree().ChangeSceneToFile("res://scenes/Draft.tscn");
        }
        catch (Exception ex)
        {
            _status.Text = $"进入单人练习失败：{ex.Message}";
            SetButtonsEnabled(true);
        }
    }

    // ==================== 其他入口 ====================

    /// <summary>以模态窗（Window）形式弹出子界面，不切换场景（避免重播大厅 BGM）。</summary>
    private void OpenModal(string scenePath, string title)
    {
        if (_modalWindow is not null) return;
        var scene = ResourceLoader.Load<PackedScene>(scenePath);
        if (scene is null) return;
        var win = (Window)scene.Instantiate();
        win.Exclusive = true; // 模态：阻塞底层输入
        win.Title = title;
        _modalWindow = win;
        win.CloseRequested += () =>
        {
            _modalWindow = null;
            win.QueueFree();
        };
        AddChild(win);
        win.PopupCentered();
    }

    private void OnBlogPressed()
    {
        string url = $"https://www.wrss.org/zytter?username={Uri.EscapeDataString(_usernameText)}&id={_accountId}";
        OS.ShellOpen(url);
    }

    private async void OnLogoutPressed()
    {
        try
        {
            // 释放在线占用，允许同账号稍后重新登录
            await Net.Instance.EnsureLobbyAsync();
            await Net.Instance.Lobby!.InvokeAsync("Logout", Net.Instance.Token);
        }
        catch
        {
        }
        Net.Instance.Token = "";
        Net.Instance.SaveSession();
        GetTree().ChangeSceneToFile("res://scenes/Login.tscn");
    }

    private void OnExitPressed()
    {
        var dialog = new ConfirmationDialog
        {
            Title = "登出学园激斗事件簿",
            DialogText = "确认退出游戏？",
            OkButtonText = "确定",
            CancelButtonText = "取消",
        };
        dialog.Confirmed += () => GetTree().Quit();
        AddChild(dialog);
        dialog.PopupCentered();
    }

    private void SetButtonsEnabled(bool enabled)
    {
        _match.Disabled = !enabled || _matching;
        _aiMatch.Disabled = !enabled;
        _heroes.Disabled = !enabled;
        _rankList.Disabled = !enabled;
        _personal.Disabled = !enabled;
        _blog.Disabled = !enabled;
        _logout.Disabled = !enabled;
    }
}
