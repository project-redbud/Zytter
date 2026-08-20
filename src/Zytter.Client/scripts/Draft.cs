using Godot;
using Microsoft.AspNetCore.SignalR.Client;
using Zytter.Core.Data;
using Zytter.Core.Drafting;

namespace Zytter.Client;

/// <summary>禁选快照 DTO（与服务端 DraftSnapshotDto 形状一致）。</summary>
public sealed record DraftSnapshotDto(
    Guid RoomId, string Side, string Phase, int StepIndex, double StepRemainingSeconds,
    int[] HeroPool, int[] BansA, int[] BansB, int[] PicksA, int[] PicksB);

/// <summary>
/// 禁选（B/P）界面：4 BAN + 6 PICK + 排序。服务器权威驱动，界面实时显示
/// 双方禁/选槽位（带头像）、倒计时；排序阶段用三个下拉框决定出场顺序（同原版 SortHero）。
/// </summary>
public partial class Draft : Control
{
    private Label _status = null!;
    private Label _timer = null!;
    private GridContainer _pool = null!;
    private HBoxContainer _myBanSlots = null!;
    private HBoxContainer _myPickSlots = null!;
    private HBoxContainer _enemyBanSlots = null!;
    private HBoxContainer _enemyPickSlots = null!;
    private VBoxContainer _orderBox = null!;
    private Button _confirmOrder = null!;

    private readonly System.Collections.Concurrent.ConcurrentQueue<DraftEvent> _pending = new();
    private readonly GameDataCatalog _catalog = GameDataCatalog.LoadDefault();

    private string _mySide = "A";
    private bool _myTurn;
    private string _currentKind = "";
    private bool _orderingPhase;
    private bool _completed;
    private double _stepRemaining;
    private readonly HashSet<int> _removed = new();
    private readonly List<int> _myBans = new();
    private readonly List<int> _enemyBans = new();
    private readonly List<int> _myPicks = new();
    private readonly List<int> _enemyPicks = new();
    private readonly List<OptionButton> _orderButtons = new();

    public override void _Ready()
    {
        _status = GetNode<Label>("%Status");
        _timer = GetNode<Label>("%Timer");
        _pool = GetNode<GridContainer>("%Pool");
        _myBanSlots = GetNode<HBoxContainer>("%MyBanSlots");
        _myPickSlots = GetNode<HBoxContainer>("%MyPickSlots");
        _enemyBanSlots = GetNode<HBoxContainer>("%EnemyBanSlots");
        _enemyPickSlots = GetNode<HBoxContainer>("%EnemyPickSlots");
        _orderBox = GetNode<VBoxContainer>("%OrderBox");
        _confirmOrder = GetNode<Button>("%ConfirmOrder");

        _confirmOrder.Pressed += () => _ = SubmitOrderAsync();
        Net.Instance.PlayDraftBgm();
        _ = JoinDraftAsync();
    }

    public override void _Process(double delta)
    {
        if (_stepRemaining > 0)
        {
            _stepRemaining -= delta;
            _timer.Text = $"剩余 {Math.Max(0, Math.Ceiling(_stepRemaining)):0} 秒";
        }
    }

    private async Task JoinDraftAsync()
    {
        var net = Net.Instance;
        try
        {
            await net.EnsureLobbyAsync();
            net.Lobby!.On<DraftEvent[]>("DraftEvents", OnDraftEvents);

            var snap = await net.Lobby!.InvokeAsync<DraftSnapshotDto>(
                "DraftJoin", net.RoomId, net.Token);

            _mySide = snap.Side;
            _removed.Clear();
            foreach (var id in snap.BansA.Concat(snap.BansB).Concat(snap.PicksA).Concat(snap.PicksB))
                _removed.Add(id);
            _myBans.AddRange(snap.BansA);
            _enemyBans.AddRange(snap.BansB);
            _myPicks.AddRange(snap.PicksA);
            _enemyPicks.AddRange(snap.PicksB);
            RebuildPool();
            RebuildSlots();
            AppendStatus($"你是 {(_mySide == "A" ? "房主·先手" : "后手")} 方（左边为对方，下方为你）");
        }
        catch (Exception ex)
        {
            AppendStatus($"加入禁选失败：{ex.Message}");
            _ = Task.Run(async () =>
            {
                await Task.Delay(3000);
                CallDeferred(nameof(BackToMenu));
            });
        }
    }

    private Task OnDraftEvents(DraftEvent[] events)
    {
        foreach (var e in events)
            _pending.Enqueue(e);
        CallDeferred(nameof(DrainEvents));
        return Task.CompletedTask;
    }

    private void DrainEvents()
    {
        while (_pending.TryDequeue(out var e))
            HandleEvent(e);
        RebuildPool();
        RebuildSlots();
    }

    private void HandleEvent(DraftEvent e)
    {
        switch (e)
        {
            case DraftStartedEvent started:
                Net.Instance.PlaySfx("gamematchisready");
                AppendStatus("禁选开始！双方各禁用 2 名英雄（房主先手）。");
                break;

            case DraftStepChangedEvent step:
                _currentKind = step.Kind;
                _orderingPhase = false;
                _myTurn = step.Side == _mySide;
                _stepRemaining = step.TimeoutSeconds;
                string who = _myTurn ? "你" : "对方";
                string action = step.Kind == "ban" ? "禁用" : "选用";
                AppendStatus($"轮到{who}{action}一名英雄");
                _confirmOrder.Visible = false;
                _orderBox.Visible = false;
                if (Net.Instance.IsBot && _myTurn)
                    _ = BotActAsync(step.Kind);
                break;

            case HeroBannedEvent ban:
                if (ban.HeroId != 0) _removed.Add(ban.HeroId);
                if (ban.Side == _mySide) _myBans.Add(ban.HeroId);
                else _enemyBans.Add(ban.HeroId);
                AppendStatus($"{SideName(ban.Side)}禁用了 {(ban.HeroId == 0 ? "（弃权）" : _catalog.GetHero(ban.HeroId).Name)}");
                break;

            case HeroPickedEvent pick:
                if (pick.HeroId != 0) _removed.Add(pick.HeroId);
                if (pick.Side == _mySide) _myPicks.Add(pick.HeroId);
                else _enemyPicks.Add(pick.HeroId);
                AppendStatus($"{SideName(pick.Side)}选用了 {(pick.HeroId == 0 ? "（弃权）" : _catalog.GetHero(pick.HeroId).Name)}");
                break;

            case DraftOrderPhaseEvent order:
                _orderingPhase = true;
                _myTurn = true;
                _stepRemaining = order.TimeoutSeconds;
                AppendStatus("排序阶段：用下方三个下拉框决定出场顺序（1 号最先上场）");
                RebuildOrderBox();
                if (Net.Instance.IsBot)
                    _ = BotOrderAsync();
                break;

            case DraftOrderedEvent ordered:
                AppendStatus($"{SideName(ordered.Side)}已提交出场顺序");
                break;
            case DraftCompletedEvent completed:
                if (_completed) return; // 防重复事件（重放/双发）
                _completed = true;
                _stepRemaining = 0;
                if (completed.RosterA.Length == 0)
                {
                    AppendStatus("对局作废（有玩家未选出英雄），返回大厅……");
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(2500);
                        CallDeferred(nameof(BackToMenu));
                    });
                }
                else
                {
                    AppendStatus("禁选完成！进入战斗……");
                    Net.Instance.Roster = _mySide == "A" ? completed.RosterA : completed.RosterB;
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(1200);
                        CallDeferred(nameof(GoToBattle));
                    });
                }
                break;
        }
    }

    // ==================== 排序（原版 SortHero 下拉框式） ====================

    private void RebuildOrderBox()
    {
        foreach (var child in _orderBox.GetChildren())
            child.QueueFree();
        _orderButtons.Clear();

        if (_myPicks.Count == 0)
        {
            _orderBox.AddChild(new Label { Text = "（你未选用英雄，将自动按选用顺序）" });
            _orderBox.Visible = true;
            _confirmOrder.Visible = true;
            return;
        }

        for (int i = 0; i < _myPicks.Count; i++)
        {
            var row = new HBoxContainer();
            var label = new Label { Text = $"出场 {i + 1}：", CustomMinimumSize = new Vector2(70, 0) };
            var option = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            foreach (var heroId in _myPicks)
                option.AddItem(_catalog.GetHero(heroId).Name);
            option.Selected = i; // 默认按选用顺序
            row.AddChild(label);
            row.AddChild(option);
            _orderBox.AddChild(row);
            _orderButtons.Add(option);
        }
        _orderBox.Visible = true;
        _confirmOrder.Visible = true;
        _confirmOrder.Disabled = false;
    }

    private async Task SubmitOrderAsync()
    {
        if (!_orderingPhase || _completed) return;
        var order = new List<int>();
        var used = new HashSet<int>();
        foreach (var option in _orderButtons)
        {
            var heroId = _myPicks[option.Selected];
            if (used.Contains(heroId)) continue; // 重复选择只取首次
            used.Add(heroId);
            order.Add(heroId);
        }
        if (order.Count != _myPicks.Count)
            order = _myPicks.ToList(); // 兜底：不完整时按选用顺序

        try
        {
            await Net.Instance.Lobby!.InvokeAsync("DraftOrder", Net.Instance.RoomId, Net.Instance.Token, order.ToArray());
            AppendStatus("出场顺序已提交，等待对方……");
            _confirmOrder.Disabled = true;
            foreach (var option in _orderButtons) option.Disabled = true;
        }
        catch (Exception ex)
        {
            AppendStatus($"提交失败：{ex.Message}");
        }
    }

    // ==================== 池与槽位 ====================

    private void RebuildPool()
    {
        foreach (var child in _pool.GetChildren())
            child.QueueFree();

        foreach (var heroId in _catalog.Heroes.Keys.OrderBy(id => id))
        {
            var hero = _catalog.GetHero(heroId);
            var button = new Button
            {
                Text = hero.Name,
                Disabled = _removed.Contains(heroId) || _orderingPhase || !_myTurn,
                CustomMinimumSize = new Vector2(96, 92),
                TooltipText = BuildHeroTooltip(heroId),
            };
            string iconPath = Net.HeroSelect(heroId);
            if (ResourceLoader.Exists(iconPath))
            {
                // 有头像图标：只显示图标（属性/技能在悬停提示中）
                button.Icon = GD.Load<Texture2D>(iconPath);
                button.Text = "";
                button.CustomMinimumSize = new Vector2(72, 72);
            }
            int id = heroId;
            button.Pressed += () => OnHeroButton(id);
            _pool.AddChild(button);
        }
    }

    private void RebuildSlots()
    {
        FillSlots(_enemyBanSlots, _enemyBans);
        FillSlots(_enemyPickSlots, _enemyPicks);
        FillSlots(_myBanSlots, _myBans);
        FillSlots(_myPickSlots, _myPicks);
    }

    private static void FillSlots(HBoxContainer container, List<int> heroes)
    {
        foreach (var child in container.GetChildren())
            child.QueueFree();
        foreach (var heroId in heroes)
        {
            var rect = new TextureRect
            {
                CustomMinimumSize = new Vector2(52, 52),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                TooltipText = heroId == 0 ? "弃权" : BuildHeroTooltip(heroId),
            };
            string path = heroId == 0 ? "" : Net.HeroSelect(heroId);
            if (ResourceLoader.Exists(path))
                rect.Texture = GD.Load<Texture2D>(path);
            else
                rect.AddChild(new Label { Text = heroId == 0 ? "弃权" : $"{heroId}", HorizontalAlignment = HorizontalAlignment.Center });
            container.AddChild(rect);
        }
    }

    private void OnHeroButton(int heroId)
    {
        if (_orderingPhase || !_myTurn || _completed) return;
        _ = ActAsync(_currentKind, heroId);
    }

    private async Task ActAsync(string kind, int heroId)
    {
        try
        {
            if (kind == "ban")
                await Net.Instance.Lobby!.InvokeAsync("DraftBan", Net.Instance.RoomId, Net.Instance.Token, heroId);
            else
                await Net.Instance.Lobby!.InvokeAsync("DraftPick", Net.Instance.RoomId, Net.Instance.Token, heroId);
            _myTurn = false;
        }
        catch (Exception ex)
        {
            AppendStatus($"操作被拒绝：{ex.Message}");
        }
    }

    private async Task BotActAsync(string kind)
    {
        await Task.Delay(300);
        if (_completed) return;
        var available = _catalog.Heroes.Keys.Where(id => !_removed.Contains(id)).ToArray();
        int heroId = available.Length > 0 ? available[0] : 0;
        await ActAsync(kind, heroId);
    }

    private async Task BotOrderAsync()
    {
        await Task.Delay(300);
        await SubmitOrderAsync();
    }

    private static string BuildHeroTooltip(int heroId)
    {
        var catalog = GameDataCatalog.LoadDefault();
        var hero = catalog.GetHero(heroId);
        var parts = new List<string>
        {
            $"{hero.Name}（{hero.Ename}）",
            $"生命 {hero.Hp}  魔法 {hero.Mp}",
            $"攻击 {hero.Atk}  护甲 {hero.Def}  魔抗 {hero.Adf}",
            $"行动力 {hero.Move}  回蓝 {hero.Remp}",
            "技能：",
        };
        foreach (var slot in new[] { SkillSlot.Q, SkillSlot.W, SkillSlot.E, SkillSlot.R })
        {
            var skill = catalog.GetSkill(hero, slot);
            if (skill is not null)
                parts.Add($"  [{slot}] {skill.Name}（{skill.Mp} 蓝）：{skill.Describe.Replace('\n', ' ')}");
        }
        return UiHelpers.Wrap(string.Join("\n", parts));
    }

    private void GoToBattle() => GetTree().ChangeSceneToFile("res://scenes/Battle.tscn");
    private void BackToMenu() => GetTree().ChangeSceneToFile("res://scenes/MainMenu.tscn");

    private string SideName(string side) => side == _mySide ? "我方" : "对方";

    private void AppendStatus(string text)
    {
        _status.Text = text;
        if (Net.Instance.IsBot)
            GD.Print($"[bot][draft:{_mySide}] {text}");
    }
}
