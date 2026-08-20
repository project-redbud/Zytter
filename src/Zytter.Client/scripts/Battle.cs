using Godot;
using Microsoft.AspNetCore.SignalR.Client;
using Zytter.Core.Battle;
using Zytter.Core.Data;
using Zytter.Core.Heroes;

namespace Zytter.Client;

/// <summary>
/// 战斗界面：消费服务器权威事件流驱动表现层，客户端不计算任何规则。
/// 交互采用原版流程：先选择行动（技能/普攻/道具）→ 查看说明 → 点【确定】提交。
/// </summary>
public partial class Battle : Control
{
    private Label _roundLabel = null!;
    private Label _phaseLabel = null!;
    private Label _selection = null!;
    private RichTextLabel _log = null!;
    private TextureRect _selfPortrait = null!;
    private TextureRect _enemyPortrait = null!;
    private Label _selfName = null!;
    private Label _enemyName = null!;
    private ProgressBar _selfHp = null!;
    private ProgressBar _selfMp = null!;
    private ProgressBar _enemyHp = null!;
    private ProgressBar _enemyMp = null!;
    private Label _selfHpText = null!;
    private Label _selfMpText = null!;
    private Label _enemyHpText = null!;
    private Label _enemyMpText = null!;
    private Label _selfMeta = null!;
    private Label _enemyStatus = null!;
    private Button _q = null!;
    private Button _w = null!;
    private Button _e = null!;
    private Button _r = null!;
    private Button _attack = null!;
    private Button _confirm = null!;
    private Button _skip = null!;
    private Button _surrender = null!;
    private Button _itemBoxBtn = null!;
    private PanelContainer _shopPanel = null!;
    private PanelContainer _itemBoxPanel = null!;
    private PanelContainer _crystalPanel = null!;
    private PanelContainer _chainQPanel = null!;
    private GridContainer _shopItems = null!;
    private VBoxContainer _items = null!;
    private Label _shopTitle = null!;
    private LineEdit _chatInput = null!;
    private Button _chatSend = null!;
    private Button _branch1 = null!;
    private Button _branch2 = null!;
    private Button _branch3 = null!;

    // ==================== 状态跟踪 ====================

    private sealed class Unit
    {
        public string Name = "";
        public int HeroId;
        public int Hp;
        public int MaxHp;
        public int Mp;
        public int MaxMp;
        public int Gold;
        public double Attack;
        public double Defense;
        public double MagicDefense;
        public double ActionPower;
        public readonly Dictionary<string, int> Buffs = new(); // buffId → 层数
        public readonly Dictionary<string, int> BuffDurations = new(); // buffId → 剩余回合（-1=永久）
        public CombatStatus Status = CombatStatus.None;
    }

    private readonly Dictionary<string, Unit> _units = new() { ["A"] = new Unit(), ["B"] = new Unit() };
    private readonly Dictionary<int, int> _box = new();          // 道具盒 itemId → 数量
    private readonly Dictionary<string, int?> _equipment = new() { ["Z"] = null, ["X"] = null };
    private readonly Dictionary<string, Dictionary<string, int?>> _equipmentBySide = new()
    {
        ["A"] = new() { ["Z"] = null, ["X"] = null },
        ["B"] = new() { ["Z"] = null, ["X"] = null },
    };
    private readonly System.Collections.Concurrent.ConcurrentQueue<BattleEvent> _pendingEvents = new();
    private readonly GameDataCatalog _catalog = GameDataCatalog.LoadDefault();

    private string _mySide = "A";
    private bool _inActionPhase;
    private bool _inShopPhase;
    private BattlePhase _currentPhase = BattlePhase.Warmup;
    private double _phaseRemaining;
    private bool _actionLocked;
    private int _roundNumber;
    private ActionDto? _pendingAction;      // 选中的行动（确定后提交）

    /// <summary>加入对局时的快照序号：重放的历史事件（Seq ≤ 该值）只记日志、不重复应用状态。</summary>
    private long _snapshotSeq;

    // 双方后备英雄追踪（战绩栏 + 原版 BGM 切换）
    private readonly Dictionary<string, int> _rosterSizes = new();
    private readonly Dictionary<string, int> _deaths = new() { ["A"] = 0, ["B"] = 0 };
    private readonly Dictionary<string, string[]> _rosterNames = new();

    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _chatQueue = new();
    private Label _scoreBoard = null!;
    private PanelContainer _resultPanel = null!;

    // 技能状态信息（purity/kill_chance/oracle），用于动态 tooltip 与神谕提示
    private readonly Dictionary<string, Dictionary<string, int>> _skillInfo = new()
    {
        ["A"] = new(),
        ["B"] = new(),
    };

    // 幸运数字全屏横幅
    private Label _luckBanner = null!;
    private double _luckBannerRemaining;

    // 二次确认（放弃/投降）
    private PanelContainer _confirmPanel = null!;
    private string _pendingConfirm = "";

    // 换人后强制补满（drain 批次末尾）
    private string? _switchedSideThisDrain;

    // Buff 图标行（原版式）
    private HBoxContainer _selfBuffs = null!;
    private HBoxContainer _enemyBuffs = null!;
    private int _buffVersion;
    private bool _buffIconsBuilt;

    public override void _Ready()
    {
        _roundLabel = GetNode<Label>("%RoundLabel");
        _phaseLabel = GetNode<Label>("%PhaseLabel");
        _selection = GetNode<Label>("%Selection");
        _log = GetNode<RichTextLabel>("%Log");
        _selfPortrait = GetNode<TextureRect>("%SelfPortrait");
        _enemyPortrait = GetNode<TextureRect>("%EnemyPortrait");
        _selfName = GetNode<Label>("%SelfName");
        _enemyName = GetNode<Label>("%EnemyName");
        _selfHp = GetNode<ProgressBar>("%SelfHp");
        _selfMp = GetNode<ProgressBar>("%SelfMp");
        _enemyHp = GetNode<ProgressBar>("%EnemyHp");
        _enemyMp = GetNode<ProgressBar>("%EnemyMp");
        _selfHpText = GetNode<Label>("%SelfHpText");
        _selfMpText = GetNode<Label>("%SelfMpText");
        _enemyHpText = GetNode<Label>("%EnemyHpText");
        _enemyMpText = GetNode<Label>("%EnemyMpText");
        _selfMeta = GetNode<Label>("%SelfMeta");
        _enemyStatus = GetNode<Label>("%EnemyStatus");
        _selfBuffs = GetNode<HBoxContainer>("%SelfBuffs");
        _enemyBuffs = GetNode<HBoxContainer>("%EnemyBuffs");
        _q = GetNode<Button>("%Q");
        _w = GetNode<Button>("%W");
        _e = GetNode<Button>("%E");
        _r = GetNode<Button>("%R");
        _attack = GetNode<Button>("%Attack");
        _confirm = GetNode<Button>("%Confirm");
        _skip = GetNode<Button>("%Skip");
        _surrender = GetNode<Button>("%Surrender");
        _itemBoxBtn = GetNode<Button>("%ItemBoxBtn");
        _shopPanel = GetNode<PanelContainer>("%ShopPanel");
        _itemBoxPanel = GetNode<PanelContainer>("%ItemBoxPanel");
        _crystalPanel = GetNode<PanelContainer>("%CrystalPanel");
        _chainQPanel = GetNode<PanelContainer>("%ChainQPanel");
        _shopItems = GetNode<GridContainer>("%ShopItems");
        _items = GetNode<VBoxContainer>("%Items");
        _shopTitle = GetNode<Label>("%ShopTitle");
        _chatInput = GetNode<LineEdit>("%ChatInput");
        _chatSend = GetNode<Button>("%ChatSend");
        _branch1 = GetNode<Button>("%Branch1");
        _branch2 = GetNode<Button>("%Branch2");
        _branch3 = GetNode<Button>("%Branch3");
        _scoreBoard = GetNode<Label>("%ScoreBoard");
        _resultPanel = GetNode<PanelContainer>("%ResultPanel");
        _luckBanner = GetNode<Label>("%LuckBanner");
        _confirmPanel = GetNode<PanelContainer>("%ConfirmPanel");
        GetNode<Button>("%ResultBack").Pressed += OnBackPressed;

        // 二次确认（放弃/投降）
        GetNode<Button>("%ConfirmYes").Pressed += OnConfirmYes;
        GetNode<Button>("%ConfirmNo").Pressed += () => { _confirmPanel.Visible = false; _pendingConfirm = ""; };

        _log.BbcodeEnabled = true;

        _attack.TooltipText = "普通攻击：造成 攻击力 − 对方护甲×(1−物穿) 的物理伤害（可被闪避）";
        _attack.Pressed += OnAttackPressed;
        _q.Pressed += () => OnSkillPressed(SkillSlot.Q);
        _w.Pressed += () => OnSkillPressed(SkillSlot.W);
        _e.Pressed += () => OnSkillPressed(SkillSlot.E);
        _r.Pressed += () => OnSkillPressed(SkillSlot.R);
        _confirm.Pressed += OnConfirmPressed;
        _skip.Pressed += OnSkipPressed;
        _surrender.Pressed += OnSurrenderPressed;
        _itemBoxBtn.Pressed += OnItemBoxToggle;
        _chatSend.Pressed += () => _ = SendChatAsync();
        _chatInput.TextSubmitted += text => { _ = SendChatAsync(); };
        _branch1.Pressed += () => _ = SendCommand("ChooseCrystal", 1);
        _branch2.Pressed += () => _ = SendCommand("ChooseCrystal", 2);
        _branch3.Pressed += () => _ = SendCommand("ChooseCrystal", 3);

        // 杨圣诺 W（星辰陨落）追加 Q 确认（原版弹窗）
        GetNode<Button>("%ChainQYes").Pressed += () =>
        {
            _chainQPanel.Visible = false;
            var skill = _catalog.GetSkill(_catalog.GetHero(3), SkillSlot.W)!;
            var q = _catalog.GetSkill(_catalog.GetHero(3), SkillSlot.Q)!;
            _pendingAction = new ActionDto("skill", "W", ChainQ: true);
            _selection.Text = $"已选择【W】{skill.Name} 并追加【Q】{q.Name}（共耗蓝 {skill.Mp + q.Mp}）— 点【确定】提交";
        };
        GetNode<Button>("%ChainQNo").Pressed += () =>
        {
            _chainQPanel.Visible = false;
            var skill = _catalog.GetSkill(_catalog.GetHero(3), SkillSlot.W)!;
            _pendingAction = new ActionDto("skill", "W");
            _selection.Text = $"已选择【W】{skill.Name}（耗蓝 {skill.Mp}）— 点【确定】提交";
        };

        // 商店不可提前关闭（无关闭按钮）；道具盒可随时关闭
        GetNode<Button>("%ItemBoxClose").Pressed += () => _itemBoxPanel.Visible = false;

        SetActionsEnabled(false);
        _ = JoinBattleAsync();
    }

    public override void _Process(double delta)
    {
        // 各阶段倒计时统一刷新（运筹帷幄/商店/励兵秣马/兵戎相见/热身）
        if (_phaseRemaining > 0 && _currentPhase != BattlePhase.Ended)
        {
            _phaseRemaining -= delta;
            string name = _currentPhase switch
            {
                BattlePhase.Warmup => "热身",
                BattlePhase.Shop => "商店购物",
                BattlePhase.Prepare => "励兵秣马",
                BattlePhase.Action => "运筹帷幄",
                BattlePhase.Resolving => "兵戎相见",
                _ => "",
            };
            _phaseLabel.Text = $"{name}（{Math.Max(0, Math.Ceiling(_phaseRemaining)):0} 秒）";
            if (_inShopPhase)
                _shopTitle.Text = $"学园商店（金币 {_units[_mySide].Gold}，剩余 {Math.Max(0, Math.Ceiling(_phaseRemaining)):0} 秒）";
        }

        // 幸运数字横幅淡出
        if (_luckBannerRemaining > 0)
        {
            _luckBannerRemaining -= delta;
            if (_luckBannerRemaining <= 0)
                _luckBanner.Visible = false;
        }
    }

    private void DrainChat()
    {
        while (_chatQueue.TryDequeue(out var text))
            AppendLog($"[聊天] {text}");
    }

    private void OnSkipPressed()
    {
        if (!_inActionPhase || _actionLocked) return;
        GetNode<Label>("%ConfirmLabel").Text = "确定本回合放弃行动吗？";
        _pendingConfirm = "skip";
        _confirmPanel.Visible = true;
    }

    private void OnSurrenderPressed()
    {
        GetNode<Label>("%ConfirmLabel").Text = "确定要投降认负吗？（第 13 回合起可投降）";
        _pendingConfirm = "surrender";
        _confirmPanel.Visible = true;
    }

    private async void OnConfirmYes()
    {
        _confirmPanel.Visible = false;
        var action = _pendingConfirm;
        _pendingConfirm = "";
        if (action == "skip")
            await SubmitAction(new ActionDto("skip"));
        else if (action == "surrender")
            await SendCommand("Surrender");
    }

    /// <summary>幸运数字全屏横幅（奕阳 Q/W/E、魔王怒、风之结界等判定）。</summary>
    private void ShowLuckBanner(LuckRollEvent roll)
    {
        string who = roll.Side.ToString() == _mySide ? "我方" : "对方";
        string result = roll.Success ? "✔ 成功" : "✘ 失败";
        _luckBanner.Text = $"【{who}】{roll.SkillName} 幸运数字判定\n掷出 {roll.Rolled} / 阈值 {roll.Threshold} → {result}";
        _luckBanner.Visible = true;
        _luckBannerRemaining = 2.5;
    }

    // ==================== 加入对局 ====================

    private async Task JoinBattleAsync()
    {
        var net = Net.Instance;
        try
        {
            await net.EnsureBattleAsync();
            net.Battle!.On<BattleEvent[]>("Events", OnEvents);

            await net.EnsureChatAsync();
            // 聊天消息也走主线程队列（直接写 UI 会因线程问题延迟到下一次推送才显示）
            net.Chat!.On<ChatMessageDto>("ChatMessage", m =>
            {
                _chatQueue.Enqueue($"💬 {m.Sender}：{m.Text}");
                CallDeferred(nameof(DrainChat));
                return Task.CompletedTask;
            });
            await net.Chat!.InvokeAsync("JoinChat", net.RoomId, net.Token);

            var snap = await net.Battle!.InvokeAsync<BattleSnapshotDto>(
                "JoinBattle", new JoinBattleRequest(net.RoomId, net.Token));

            _mySide = snap.Side;
            _snapshotSeq = snap.LastSeq;
            var enemySide = snap.Side == "A" ? "B" : "A";
            _rosterSizes[snap.Side] = snap.Side == "A" ? snap.RosterA.Length : snap.RosterB.Length;
            _rosterSizes[enemySide] = enemySide == "A" ? snap.RosterA.Length : snap.RosterB.Length;
            _rosterNames[snap.Side] = snap.Side == "A" ? snap.TeamA : snap.TeamB;
            _rosterNames[enemySide] = enemySide == "A" ? snap.TeamA : snap.TeamB;
            _deaths["A"] = 0;
            _deaths["B"] = 0;
            SetUnit(snap.Side, snap.MyHeroId, snap.MyHeroName, snap.MyHp, snap.MyMaxHp, snap.MyMp, snap.MyMaxMp);
            SetUnit(enemySide, snap.EnemyHeroId, snap.EnemyHeroName, snap.EnemyHp, snap.EnemyMaxHp, snap.EnemyMp, snap.EnemyMaxMp);

            ReconfigureSkillButtons();
            UpdateAllLabels();
            UpdateScoreBoard();
            AppendLog($"已加入对局（第 {snap.Round} 回合，上限 {snap.RoundLimit}，你是 {snap.Side} 方）");
        }
        catch (Exception ex)
        {
            AppendLog($"加入对局失败：{ex.Message}", "red");
            GetNode<Label>("%ResultTitle").Text = "连接失败";
            GetNode<Label>("%ResultReason").Text = $"（{ex.Message}）";
            GetNode<Label>("%ResultStats").Text = "请检查服务器是否运行，然后返回主菜单重试。";
            _resultPanel.Visible = true;
        }
    }

    /// <summary>设置单位数值。resetBuffs=false 用于权威同步事件（只校正数值，不清空 Buff/状态追踪）。</summary>
    private void SetUnit(string side, int heroId, string name, int hp, int maxHp, int mp, int maxMp, bool resetBuffs = true)
    {
        var unit = _units[side];
        unit.HeroId = heroId;
        unit.Name = name;
        unit.Hp = hp;
        unit.MaxHp = maxHp;
        unit.Mp = mp;
        unit.MaxMp = maxMp;
        if (resetBuffs)
        {
            unit.Buffs.Clear();
            unit.BuffDurations.Clear();
            unit.Status = CombatStatus.None;
            _buffVersion++;
        }
    }

    // ==================== 技能按钮（当前英雄） ====================

    private void ReconfigureSkillButtons()
    {
        var heroId = _units[_mySide].HeroId;
        if (heroId == 0) return;

        foreach (var (slot, button) in new[]
                 {
                     (SkillSlot.Q, _q), (SkillSlot.W, _w), (SkillSlot.E, _e), (SkillSlot.R, _r),
                 })
        {
            var skill = _catalog.GetSkill(_catalog.GetHero(heroId), slot);
            if (skill is null)
            {
                button.Visible = false;
                continue;
            }
            button.Visible = true;
            button.TooltipText = UiHelpers.SkillTooltip(skill);
            string iconPath = Net.SkillIcon(heroId, slot);
            if (ResourceLoader.Exists(iconPath))
            {
                // 有图标：只显示图标，不显示文字（说明在悬停提示中）
                button.Icon = GD.Load<Texture2D>(iconPath);
                button.Text = "";
                button.CustomMinimumSize = new Vector2(76, 48);
            }
            else
            {
                button.Icon = null;
                button.Text = $"【{slot}】{skill.Name}";
            }
        }

        // 英雄头像与说明
        _selfName.Text = $"我方：{_units[_mySide].Name}";
        _enemyName.Text = $"对方：{_units[_mySide == "A" ? "B" : "A"].Name}";
        SetPortrait(_selfPortrait, heroId);
        SetPortrait(_enemyPortrait, _units[_mySide == "A" ? "B" : "A"].HeroId);
        _selfPortrait.TooltipText = UiHelpers.HeroTooltip(heroId);
        _enemyPortrait.TooltipText = UiHelpers.HeroTooltip(_units[_mySide == "A" ? "B" : "A"].HeroId);
    }

    private static void SetPortrait(TextureRect rect, int heroId)
    {
        string path = Net.HeroPortrait(heroId);
        if (ResourceLoader.Exists(path))
            rect.Texture = GD.Load<Texture2D>(path);
    }

    // ==================== 事件流 ====================

    private Task OnEvents(BattleEvent[] events)
    {
        foreach (var e in events)
            _pendingEvents.Enqueue(e);
        CallDeferred(nameof(DrainEvents));
        return Task.CompletedTask;
    }

    private void DrainEvents()
    {
        _switchedSideThisDrain = null;
        while (_pendingEvents.TryDequeue(out var e))
        {
            try
            {
                // 重放的历史事件：只写日志，不重复应用状态（快照已包含当前状态）
                HandleEvent(e, live: e.Seq > _snapshotSeq);
            }
            catch (Exception ex)
            {
                // 防毒化：单个事件异常不得阻塞后续事件处理（否则会造成客户端与服务器状态漂移）
                GD.PrintErr($"[battle] 事件 {e.GetType().Name} 处理失败（已跳过）：{ex}");
            }
        }

        // 强制补满：本批次发生换人后，将新英雄的血蓝强制拉满（用户反馈的换人残血兜底）
        if (_switchedSideThisDrain is { } switchedSide)
        {
            var unit = _units[switchedSide];
            unit.Hp = unit.MaxHp;
            unit.Mp = unit.MaxMp;
        }

        UpdateAllLabels();
    }

    private void HandleEvent(BattleEvent e, bool live)
    {
        switch (e)
        {
            case BattleStartedEvent started:
                AppendLog($"对局开始，回合上限 {started.RoundLimit}。");
                // BGM 切换不依赖 live：加入对局较晚时该事件以重放形式到达，也必须切掉禁选 BGM
                Net.Instance.PlayBattleBgm();
                if (live)
                {
                    Net.Instance.PlaySfx("startmatch");
                }
                break;
            case RoundStartedEvent round:
                _roundNumber = round.Round;
                _roundLabel.Text = $"第 {round.Round} / {round.RoundLimit} 回合";
                AppendLog($"━━━ 第 {round.Round} / {round.RoundLimit} 回合 ━━━", "gold");
                break;
            case ShopOpenedEvent shop:
                AppendLog($"🛒 商店开放：+{shop.GoldGranted} 金币，购物时限 {shop.ShoppingSeconds} 秒", "gold");
                if (live && shop.Side.ToString() == _mySide)
                {
                    _phaseRemaining = shop.ShoppingSeconds;
                    _inShopPhase = true;
                    RebuildShopPanel();
                    _shopPanel.Visible = true;
                }
                break;
            case PhaseChangedEvent phase:
                if (live)
                {
                    _currentPhase = phase.Phase;
                    _inActionPhase = phase.Phase == BattlePhase.Action;
                    _inShopPhase = phase.Phase == BattlePhase.Shop;
                    _actionLocked = false;
                    _pendingAction = null;
                    _phaseRemaining = phase.RemainingSeconds;
                    SetActionsEnabled(_inActionPhase);
                    _selection.Text = "点击技能/普攻选择本回合行动，再点【确定】提交";
                    if (phase.Phase != BattlePhase.Shop)
                    {
                        _shopPanel.Visible = false; // 商店阶段结束自动关闭
                    }
                    if (Net.Instance.IsBot && _inActionPhase)
                    {
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(500);
                            CallDeferred(nameof(BotAutoAttack));
                        });
                    }
                }
                AppendLog($"阶段：{PhaseName(phase.Phase)}");
                break;
            case SkillCastEvent cast:
                AppendLog($"{SideName(cast.Side)}释放了【{cast.SkillName}】", "sky");
                if (live)
                {
                    ApplyMpDelta(cast.Side.ToString(), -cast.MpCost);
                    Net.Instance.PlaySfx("magic");
                }
                break;
            case BasicAttackEvent attack:
                if (attack.DodgeThreshold <= 0)
                    AppendLog($"{SideName(attack.Side)}发起普攻（对方无闪避能力，判定掷出 {attack.DodgeRoll}）");
                else if (attack.Dodged)
                    AppendLog($"{SideName(attack.Side)}的普攻被闪避！闪避判定：掷出 {attack.DodgeRoll}，需小于 {attack.DodgeThreshold}", "gold");
                else
                    AppendLog($"{SideName(attack.Side)}发起普攻（闪避判定：掷出 {attack.DodgeRoll}，需小于 {attack.DodgeThreshold}，未闪避）");
                if (live)
                    Net.Instance.PlaySfx("atk");
                break;
            case DamageDealtEvent damage:
                AppendLog($"{SideName(damage.TargetSide)}受到 {damage.Amount} 点伤害", "red");
                if (live)
                    ApplyHpDelta(damage.TargetSide.ToString(), -damage.Amount);
                break;
            case HealedEvent heal:
                AppendLog($"{SideName(heal.Side)}回复 {heal.Amount} 点生命", "green");
                if (live)
                    ApplyHpDelta(heal.Side.ToString(), heal.Amount);
                break;
            case MpChangedEvent mp:
                if (live)
                    ApplyMpDelta(mp.Side.ToString(), mp.Delta);
                break;
            case GoldChangedEvent gold:
                if (live)
                {
                    _units[gold.Side.ToString()].Gold = gold.Gold;
                    if (gold.Delta != 0 && gold.Side.ToString() != _mySide)
                        AppendLog($"💰 {SideName(gold.Side)}金币 {gold.Gold}（{gold.Delta:+0;-0}）", "gold");
                    // 商店打开时立即刷新标题与可购买状态（修复买完后剩余金币不刷新）
                    if (_shopPanel.Visible && gold.Side.ToString() == _mySide)
                        RebuildShopPanel();
                }
                break;
            case ItemObtainedEvent obtained:
                if (live && obtained.Side.ToString() == _mySide)
                {
                    _box[obtained.ItemId] = _box.GetValueOrDefault(obtained.ItemId) + 1;
                    AppendLog($"获得【{obtained.ItemName}】", "sky");
                    if (_itemBoxPanel.Visible) RebuildItemBoxPanel();
                }
                break;
            case ItemLostEvent lost:
                if (live && lost.Side.ToString() == _mySide)
                {
                    _box[lost.ItemId] = _box.GetValueOrDefault(lost.ItemId) - 1;
                    if (_box[lost.ItemId] <= 0) _box.Remove(lost.ItemId);
                    if (_itemBoxPanel.Visible) RebuildItemBoxPanel();
                }
                break;
            case ItemUsedEvent item:
                AppendLog($"{SideName(item.Side)}使用了【{item.ItemName}】");
                break;
            case EquipmentChangedEvent equip:
                if (live)
                {
                    _equipmentBySide[equip.Side.ToString()][equip.Slot] = equip.ItemId;
                    if (equip.Side.ToString() == _mySide)
                        _equipment[equip.Slot] = equip.ItemId;
                }
                break;
            case HeroStatsSyncEvent sync:
                // 权威同步：以服务器数值为准校正本地显示（界限突破/商店成长/换人等上限变化）
                if (live)
                {
                    SetUnit(sync.Side.ToString(), sync.HeroId, sync.HeroName, sync.Hp, sync.MaxHp, sync.Mp, sync.MaxMp, resetBuffs: false);
                    var unit = _units[sync.Side.ToString()];
                    unit.Attack = sync.Attack;
                    unit.Defense = sync.Defense;
                    unit.MagicDefense = sync.MagicDefense;
                    unit.ActionPower = sync.ActionPower;
                    UpdateAttackTooltip();
                }
                break;
            case LuckRollEvent roll:
                if (live)
                    ShowLuckBanner(roll);
                AppendLog($"{SideName(roll.Side)}的【{roll.SkillName}】掷出 {roll.Rolled}（阈值 {roll.Threshold}）→ {(roll.Success ? "成功" : "失败")}", roll.Success ? "green" : "red");
                break;
            case SkillInfoEvent info:
                if (live)
                {
                    _skillInfo[info.Side.ToString()][info.Key] = info.Value;
                    switch (info.Key)
                    {
                        case "purity":
                            if (info.Side.ToString() == _mySide)
                                AppendLog($"洁净点：{info.Value} / 8", "sky");
                            UpdateSkillTooltips();
                            break;
                        case "kill_chance":
                            if (info.Side.ToString() == _mySide)
                                AppendLog($"魔王怒秒杀概率：{info.Value * 10}%", "sky");
                            UpdateSkillTooltips();
                            break;
                        case "oracle":
                            if (info.Value > 0)
                            {
                                string rule = info.Value switch { 1 => "必须普攻", 2 => "必须使用技能", _ => "必须放弃行动" };
                                AppendLog($"🔮 神谕：{SideName(info.Side)}下回合{rule}，违反将永久损失 2 点护甲！", "gold");
                            }
                            break;
                        case "oracle_result":
                            if (info.Value == 1)
                                AppendLog($"⛓ {SideName(info.Side)}违反了神谕，永久损失 2 点护甲！", "red");
                            else
                                AppendLog($"{SideName(info.Side)}遵守了神谕。", "green");
                            break;
                        case "guards":
                            // 公主号令禁卫军数量变化 → 刷新 Buff 图标 tooltip
                            _buffVersion++;
                            break;
                    }
                }
                break;
            case HeroDiedEvent died:
                AppendLog($"☠ {SideName(died.Side)}的【{died.HeroName}】阵亡！", "red");
                if (live)
                {
                    _deaths[died.Side.ToString()]++;
                    Net.Instance.PlaySfx("dead");
                    UpdateBattleBgm();
                    UpdateScoreBoard();
                }
                break;
            case HeroSwitchedEvent switched:
                AppendLog($"{SideName(switched.Side)}换上了【{switched.HeroName}】（HP {switched.MaxHp}/{switched.MaxHp}，MP {switched.MaxMp}/{switched.MaxMp}）", "sky");
                if (live)
                {
                    _switchedSideThisDrain = switched.Side.ToString();
                    Net.Instance.PlaySfx("kill");
                    UpdateBattleBgm();
                    if (switched.Side.ToString() == _mySide)
                    {
                        var heroId = _catalog.Heroes.Values.First(h => h.Name == switched.HeroName).Id;
                        SetUnit(_mySide, heroId, switched.HeroName, switched.MaxHp, switched.MaxHp, switched.MaxMp, switched.MaxMp);
                        ReconfigureSkillButtons();
                    }
                    else
                    {
                        var enemySide = _mySide == "A" ? "B" : "A";
                        var heroId = _catalog.Heroes.Values.First(h => h.Name == switched.HeroName).Id;
                        SetUnit(enemySide, heroId, switched.HeroName, switched.MaxHp, switched.MaxHp, switched.MaxMp, switched.MaxMp);
                        SetPortrait(_enemyPortrait, heroId);
                    }
                }
                break;
            case ActionSkippedEvent skipped:
                AppendLog($"{SideName(skipped.Side)}放弃行动（{skipped.Reason}）");
                break;
            case BuffAppliedEvent buff:
            {
                if (live)
                {
                    var unit = _units[buff.Side.ToString()];
                    // 服务器 BuffAppliedEvent.Stacks 已是累计后的总层数，直接覆盖而非累加
                    unit.Buffs[buff.BuffId] = buff.Stacks;
                    unit.BuffDurations[buff.BuffId] = buff.DurationRounds;
                    _buffVersion++;
                }
                AppendLog($"{SideName(buff.Side)}获得【{buff.BuffName}】", "gold");
                break;
            }
            case BuffRemovedEvent buffRemoved:
            {
                if (live)
                {
                    var unit = _units[buffRemoved.Side.ToString()];
                    unit.Buffs.Remove(buffRemoved.BuffId);
                    unit.BuffDurations.Remove(buffRemoved.BuffId);
                    _buffVersion++;
                }
                AppendLog($"{SideName(buffRemoved.Side)}的【{buffRemoved.BuffName}】消失");
                break;
            }
            case BuffSyncEvent sync:
            {
                if (live)
                {
                    var unit = _units[sync.Side.ToString()];
                    foreach (var (buffId, rounds) in sync.Rounds)
                    {
                        // 重连场景补全服务器仍有、但本地缺失的 Buff（层数取 1 兜底）
                        unit.Buffs.TryAdd(buffId, 1);
                        unit.BuffDurations[buffId] = rounds;
                    }
                    _buffVersion++;
                }
                break;
            }
            case StatusChangedEvent status:
                if (live)
                    _units[status.Side.ToString()].Status = status.Status;
                break;
            case CrystalReadyEvent crystal:
                if (live && crystal.Side.ToString() == _mySide)
                {
                    AppendLog("★ 结晶之力已激活！请选择分支。", "gold");
                    var texts = CrystalBranchTexts(_units[_mySide].HeroId);
                    var branchButtons = new[] { (_branch1, texts[0]), (_branch2, texts[1]), (_branch3, texts[2]) };
                    foreach (var (button, text) in branchButtons)
                    {
                        button.Text = text;
                        button.TooltipText = $"结晶之力分支：{text}";
                    }
                    _crystalPanel.Visible = true;
                }
                break;
            case CrystalChosenEvent chosen:
                AppendLog($"{SideName(chosen.Side)}选择了结晶分支 {chosen.Branch}", "gold");
                if (live && chosen.Side.ToString() == _mySide)
                    _crystalPanel.Visible = false; // 选择后立即关闭弹窗
                break;
            case PauseStateChangedEvent pause:
                AppendLog(pause.Paused ? $"⏸ {SideName(pause.Side)}发起了暂停" : "▶ 暂停解除");
                break;
            case BattleEndedEvent ended:
                string winner = ended.Winner is null ? "平局" : SideName(ended.Winner.Value);
                AppendLog($"🏁 对局结束！胜者：{winner}（{ended.Reason}）", "gold");
                if (live)
                {
                    if (ended.Winner is { } w)
                    {
                        Net.Instance.PlaySfx(w.ToString() == _mySide ? "win" : "lose");
                        Net.Instance.PlayLoopBgm(w.ToString() == _mySide ? "win" : "lose");
                    }
                    SetActionsEnabled(false);
                    _crystalPanel.Visible = false;
                    _shopPanel.Visible = false;
                    ShowResultPanel(ended);
                }
                break;
        }
    }

    /// <summary>结晶之力分支效果说明（docs/01-combat-system.md §4.5）。</summary>
    private static string[] CrystalBranchTexts(int heroId) => heroId switch
    {
        1 => new[]
        {
            "分支1：魔法穿透 +30%（永久）",
            "分支2：烈日之箭 100% 命中",
            "分支3：屠杀之风加成 +6",
        },
        6 => new[]
        {
            "分支1：云霄之巅 +2攻击+2行动力（R耗蓝+2）",
            "分支2：星月奇迹 14伤害且无视行动力（E耗蓝+2）",
            "分支3：先入为主改为技能伤害+30%",
        },
        9 => new[]
        {
            "分支1：解除流星单数回合限制",
            "分支2：流星伤害 12→20",
            "分支3：全部技能耗蓝 -2",
        },
        11 => new[]
        {
            "分支1：光炽剑 +3伤害+2回复",
            "分支2：基础攻击 +4（永久）",
            "分支3：闪现+ 持续3回合且可叠加",
        },
        _ => new[] { "分支 1", "分支 2", "分支 3" },
    };

    // ==================== 行动选择与确认（原版两步流程） ====================

    private void OnSkillPressed(SkillSlot slot)
    {
        if (!_inActionPhase || _actionLocked) return;
        var heroId = _units[_mySide].HeroId;
        var skill = _catalog.GetSkill(_catalog.GetHero(heroId), slot);
        if (skill is null) return;

        if (_units[_mySide].Mp < skill.Mp)
        {
            _selection.Text = $"魔法不足：{skill.Name} 需要 {skill.Mp} 点魔法（当前 {_units[_mySide].Mp}）";
            AppendLog($"魔法不足，无法释放【{skill.Name}】。", "red");
            return;
        }

        // 杨圣诺 W（星辰陨落）：询问是否追加一次 Q（原版弹窗确认）
        if (heroId == 3 && slot == SkillSlot.W)
        {
            var q = _catalog.GetSkill(_catalog.GetHero(heroId), SkillSlot.Q);
            if (q is not null && _units[_mySide].Mp >= skill.Mp + q.Mp)
            {
                _chainQPanel.Visible = true;
                return;
            }
        }

        _pendingAction = new ActionDto("skill", slot.ToString());
        _selection.Text = $"已选择【{slot}】{skill.Name}（耗蓝 {skill.Mp}）：{skill.Describe.Replace('\n', ' ')} — 点【确定】提交";
    }

    private void OnAttackPressed()
    {
        if (!_inActionPhase || _actionLocked) return;
        _pendingAction = new ActionDto("attack");
        _selection.Text = "已选择【普攻】— 点【确定】提交";
    }

    private async void OnConfirmPressed()
    {
        if (_pendingAction is null)
        {
            _selection.Text = "请先选择行动";
            return;
        }
        await SubmitAction(_pendingAction);
    }

    private async Task SubmitAction(ActionDto action)
    {
        if (_actionLocked && action.Kind != "skip") return;
        try
        {
            await Net.Instance.Battle!.InvokeAsync("SubmitAction", Net.Instance.Token, action);
            _actionLocked = true;
            _pendingAction = null;
            _selection.Text = "行动已提交，等待对方……";
            SetActionsEnabled(false);
        }
        catch (Exception ex)
        {
            AppendLog($"行动被拒绝：{ex.Message}", "red");
            _selection.Text = $"行动被拒绝：{ex.Message}";
        }
    }

    private async void BotAutoAttack()
    {
        if (_inActionPhase && !_actionLocked)
            await SubmitAction(new ActionDto("attack"));
    }

    // ==================== 商店 ====================

    /// <summary>重建商店：原版式纯图标网格（按物品 ID 顺序，悬停显示名称/价格/说明）。</summary>
    private void RebuildShopPanel()
    {
        foreach (var child in _shopItems.GetChildren())
            child.QueueFree();

        var gold = _units[_mySide].Gold;
        _shopTitle.Text = $"学园商店（当前金币：{gold}）";

        // 原版顺序：消耗品（1~12）在前，装备（13~27）在后
        foreach (var item in _catalog.Items.Values.OrderBy(i => i.Id))
        {
            bool affordable = gold >= item.Gold;
            string iconPath = Net.ItemIcon(item.Id);
            var button = new Button
            {
                Text = "",
                Disabled = !affordable,
                CustomMinimumSize = new Vector2(76, 76),
                TooltipText = UiHelpers.ItemTooltip(item, _box.GetValueOrDefault(item.Id)),
            };
            if (ResourceLoader.Exists(iconPath))
                button.Icon = GD.Load<Texture2D>(iconPath);
            else
                button.Text = item.Name;
            int itemId = item.Id;
            button.Pressed += () => _ = BuyItemAsync(itemId);
            _shopItems.AddChild(button);
        }
    }

    private async Task BuyItemAsync(int itemId)
    {
        try
        {
            await Net.Instance.Battle!.InvokeAsync("BuyItem", Net.Instance.Token, itemId);
            RebuildShopPanel();
        }
        catch (Exception ex)
        {
            AppendLog($"购买失败：{ex.Message}", "red");
        }
    }

    // ==================== 道具盒 / 装备 ====================

    private void OnItemBoxToggle()
    {
        if (_itemBoxPanel.Visible)
        {
            _itemBoxPanel.Visible = false;
            return;
        }
        RebuildItemBoxPanel();
        _itemBoxPanel.Visible = true;
    }

    private void RebuildItemBoxPanel()
    {
        foreach (var child in _items.GetChildren())
            child.QueueFree();

        if (_box.Count == 0)
        {
            _items.AddChild(new Label { Text = "（道具盒为空，商店回合可购买物品）" });
        }

        foreach (var (itemId, count) in _box.OrderBy(kv => kv.Key))
        {
            var def = _catalog.GetItem(itemId);
            string action = def.Kind == ItemKind.Consumable ? "使用" : "穿戴";
            var button = new Button
            {
                Text = $"[{action}] {def.Name} ×{count}",
                CustomMinimumSize = new Vector2(0, 40),
                Alignment = HorizontalAlignment.Left,
                TooltipText = UiHelpers.ItemTooltip(def, count),
            };
            string iconPath = Net.ItemIcon(itemId);
            if (ResourceLoader.Exists(iconPath))
                button.Icon = GD.Load<Texture2D>(iconPath);
            button.Pressed += () => _ = UseBoxItemAsync(itemId);
            _items.AddChild(button);
        }

        foreach (var slot in new[] { "Z", "X" })
        {
            var wornId = _equipment.GetValueOrDefault(slot);
            if (wornId is { } id)
            {
                var def = _catalog.GetItem(id);
                var button = new Button
                {
                    Text = $"[脱下{slot}槽] {def.Name}",
                    CustomMinimumSize = new Vector2(0, 36),
                    Alignment = HorizontalAlignment.Left,
                    TooltipText = "脱下该装备放回道具盒（仅行动阶段可操作）",
                };
                string iconPath = Net.ItemIcon(id);
                if (ResourceLoader.Exists(iconPath))
                    button.Icon = GD.Load<Texture2D>(iconPath);
                string slotName = slot;
                button.Pressed += () => _ = SendCommand("Equip", slotName, 0);
                _items.AddChild(button);
            }
        }
    }

    private async Task UseBoxItemAsync(int itemId)
    {
        var def = _catalog.GetItem(itemId);
        try
        {
            if (def.Kind == ItemKind.Consumable)
            {
                // 消耗品：加入行动选择，点【确定】后生效
                _pendingAction = new ActionDto("item", ItemId: itemId);
                _selection.Text = $"已选择使用【{def.Name}】— 点【确定】提交（{def.Describe}）";
                _itemBoxPanel.Visible = false;
            }
            else
            {
                // 装备：直接穿戴（仅行动阶段）
                string slot = _equipment["Z"] is null ? "Z" : "X";
                await Net.Instance.Battle!.InvokeAsync("Equip", Net.Instance.Token, slot, itemId);
                AppendLog($"已装备【{def.Name}】到 {slot} 槽。", "sky");
                RebuildItemBoxPanel();
            }
        }
        catch (Exception ex)
        {
            AppendLog($"操作失败：{ex.Message}", "red");
            _selection.Text = $"操作失败：{ex.Message}";
        }
    }

    // ==================== 聊天 ====================

    private async Task SendChatAsync()
    {
        var text = _chatInput.Text.Trim();
        if (text.Length == 0) return;
        _chatInput.Text = "";
        try
        {
            await Net.Instance.Chat!.InvokeAsync("SendChat", Net.Instance.RoomId, Net.Instance.Token, text);
        }
        catch (Exception ex)
        {
            AppendLog($"发送失败：{ex.Message}", "red");
        }
    }

    // ==================== 状态更新 ====================

    private void ApplyHpDelta(string side, int delta)
    {
        var unit = _units[side];
        unit.Hp = Math.Clamp(unit.Hp + delta, 0, unit.MaxHp);
    }

    private void ApplyMpDelta(string side, int delta)
    {
        var unit = _units[side];
        unit.Mp = Math.Clamp(unit.Mp + delta, 0, unit.MaxMp);
    }

    /// <summary>
    /// 原版 BGM 切换逻辑（Fight.java death()）：按双方后备英雄数切歌。
    /// 双方均无后备=lastbattle；我方无后备对方有=lastonehero；我方有对方无=willwin；否则维持战斗轮换。
    /// </summary>
    private void UpdateBattleBgm()
    {
        var enemySide = _mySide == "A" ? "B" : "A";
        int myBench = Math.Max(0, _rosterSizes.GetValueOrDefault(_mySide) - _deaths[_mySide] - 1);
        int enemyBench = Math.Max(0, _rosterSizes.GetValueOrDefault(enemySide) - _deaths[enemySide] - 1);

        if (myBench == 0 && enemyBench == 0)
            Net.Instance.PlayLoopBgm("lastbattle");
        else if (myBench == 0 && enemyBench > 0)
            Net.Instance.PlayLoopBgm("lastonehero");
        else if (myBench > 0 && enemyBench == 0)
            Net.Instance.PlayLoopBgm("willwin");
        // 其余情况维持 fight1~3 轮换
    }

    /// <summary>右上角战绩栏：双方击杀/阵亡 + 双方下一名待上场英雄。</summary>
    private void UpdateScoreBoard()
    {
        var enemySide = _mySide == "A" ? "B" : "A";
        string NextNames(string side)
        {
            if (!_rosterNames.TryGetValue(side, out var names)) return "无";
            int idx = _deaths.GetValueOrDefault(side) + 1;
            return idx < names.Length ? string.Join("、", names.Skip(idx)) : "无";
        }
        _scoreBoard.Text =
            $"击杀 {_deaths[enemySide]} / 阵亡 {_deaths[_mySide]}   " +
            $"我方后备：{NextNames(_mySide)}   对方后备：{NextNames(enemySide)}";
    }

    /// <summary>普攻 tooltip 动态数值（基于服务器同步的有效属性）。</summary>
    private void UpdateAttackTooltip()
    {
        var mine = _units[_mySide];
        var enemy = _units[_mySide == "A" ? "B" : "A"];
        double baseDamage = Math.Max(0, mine.Attack - enemy.Defense);
        _attack.TooltipText =
            $"普通攻击\n攻击力 {mine.Attack:0} − 对方护甲 {enemy.Defense:0} ≈ {baseDamage:0} 点物理伤害\n" +
            $"（可被闪现闪避；受鹰角弓/坚韧者之盾/予恋之花/物减影响）";
    }

    /// <summary>技能 tooltip 追加实时状态：谢悠涵洁净点 / 刘晓释魔王怒概率。</summary>
    private void UpdateSkillTooltips()
    {
        var heroId = _units[_mySide].HeroId;
        var info = _skillInfo[_mySide];

        if (heroId == 7) // 谢悠涵：Q 显示当前洁净点
        {
            var q = _catalog.GetSkill(_catalog.GetHero(7), SkillSlot.Q);
            if (q is not null)
                _q.TooltipText = UiHelpers.Wrap($"{q.Name}（魔法消耗 = 洁净点）\n当前洁净点：{info.GetValueOrDefault("purity")} / 8\n{q.Describe}");
        }
        if (heroId == 2) // 刘晓释：E 显示当前秒杀概率
        {
            var e = _catalog.GetSkill(_catalog.GetHero(2), SkillSlot.E);
            if (e is not null)
                _e.TooltipText = UiHelpers.Wrap($"{e.Name}（魔法消耗 {e.Mp}）\n当前秒杀概率：{info.GetValueOrDefault("kill_chance", 3) * 10}%\n{e.Describe}");
        }
    }

    /// <summary>对局结束战绩表（参考原版 BalanceGame 结算窗口）。</summary>
    private void ShowResultPanel(BattleEndedEvent ended)
    {
        var enemySide = _mySide == "A" ? "B" : "A";
        string winnerText = ended.Winner is null ? "平局" : (ended.Winner.Value.ToString() == _mySide ? "胜利" : "败北");
        string reasonText = ended.Reason switch
        {
            VictoryReason.Annihilation => ended.Winner is { } w && w.ToString() == _mySide
                ? "对方英雄全部阵亡"
                : "我方英雄全部阵亡",
            VictoryReason.RoundExhausted => "回合耗尽判定",
            VictoryReason.Surrender => ended.Winner is { } w2 && w2.ToString() == _mySide
                ? "对方投降"
                : "我方投降",
            VictoryReason.Disconnect => ended.Winner is { } w3 && w3.ToString() == _mySide
                ? "对方掉线"
                : "我方掉线",
            _ => ended.Reason.ToString(),
        };
        GetNode<Label>("%ResultTitle").Text = winnerText;
        GetNode<Label>("%ResultTitle").AddThemeColorOverride("font_color",
            winnerText == "胜利" ? new Color(0.62f, 0.85f, 0.66f) : new Color(0.9f, 0.35f, 0.4f));
        GetNode<Label>("%ResultReason").Text = $"（{reasonText}）";
        GetNode<Label>("%ResultStats").Text =
            $"第 {_roundNumber} 回合结束\n" +
            $"我方：击杀 {_deaths[enemySide]}，阵亡 {_deaths[_mySide]}，金币 {_units[_mySide].Gold}\n" +
            $"对方：击杀 {_deaths[_mySide]}，阵亡 {_deaths[enemySide]}，金币 {_units[enemySide].Gold}";
        _resultPanel.Visible = true;
    }

    /// <summary>原版式 Buff 图标行：一排小图标，悬停显示名称/说明/层数/剩余回合。
    /// 若 Buff 名与某技能同名（原版 Buff 即技能名简写），tooltip 附带该技能完整说明。</summary>
    private void RebuildBuffIcons()
    {
        foreach (var (side, container) in new[] { (_mySide, _selfBuffs), (_mySide == "A" ? "B" : "A", _enemyBuffs) })
        {
            foreach (var child in container.GetChildren())
                child.QueueFree();
            var unit = _units[side];
            foreach (var (buffId, stacks) in unit.Buffs.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                var def = _catalog.GetBuff(buffId);
                string iconPath = Net.BuffIcon(buffId);
                string duration = BuffDurationText(side, buffId, stacks, unit);
                string text = $"【{def.Name}】{(stacks > 1 ? $"×{stacks}" : "")}\n{duration}\n{def.Desc}";
                var skill = _catalog.Skills.Values.FirstOrDefault(s => s.Name == def.Name);
                if (skill is not null)
                    text += $"\n\n技能说明：{skill.Describe}";
                string tooltip = UiHelpers.Wrap(text);

                if (ResourceLoader.Exists(iconPath))
                {
                    var rect = new TextureRect
                    {
                        CustomMinimumSize = new Vector2(28, 28),
                        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                        MouseFilter = Control.MouseFilterEnum.Stop,
                        TooltipText = tooltip,
                    };
                    rect.Texture = GD.Load<Texture2D>(iconPath);
                    container.AddChild(rect);
                }
                else
                {
                    // 无图标回退：显示技能/状态首两字，同样支持悬停看完整说明
                    var label = new Label
                    {
                        Text = def.Name[..Math.Min(2, def.Name.Length)],
                        CustomMinimumSize = new Vector2(28, 28),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        MouseFilter = Control.MouseFilterEnum.Stop,
                        TooltipText = tooltip,
                    };
                    container.AddChild(label);
                }
            }
        }
    }

    /// <summary>
    /// 计算 Buff 图标 tooltip 中的持续文案：
    /// 限时 Buff 显示「剩余 N 回合」；公主号令显示剩余禁卫军数量；
    /// 层数型无回合 Buff（如灼烧）显示「×N 层」；其余永久型显示「永久」。
    /// </summary>
    private string BuffDurationText(string side, string buffId, int stacks, Unit unit)
    {
        if (buffId == "princess_order")
        {
            int guards = _skillInfo[side].TryGetValue("guards", out int g) ? g : stacks;
            return $"禁卫军 ×{guards}";
        }

        if (unit.BuffDurations.TryGetValue(buffId, out int d) && d >= 0)
            return $"剩余 {d} 回合";

        return stacks > 1 ? $"×{stacks} 层" : "永久";
    }

    /// <summary>动态角色属性 tooltip：有效属性（含装备/技能加成）+ 装备 + 金币。</summary>
    private string DynamicHeroTooltip(string side)
    {
        var unit = _units[side];
        if (unit.HeroId == 0)
            return "等待对局开始…";
        var parts = new List<string> { $"{unit.Name}（{_catalog.GetHero(unit.HeroId).Ename}）" };
        parts.Add($"生命 {unit.Hp}/{unit.MaxHp}  魔法 {unit.Mp}/{unit.MaxMp}");
        parts.Add($"攻击 {unit.Attack:0}  护甲 {unit.Defense:0}  魔抗 {unit.MagicDefense:0}  行动力 {unit.ActionPower:0}");
        parts.Add($"金币 {unit.Gold}");

        var sideEquip = _equipmentBySide[side];
        var worn = new List<string>();
        foreach (var (slot, id) in sideEquip)
        {
            if (id is { } itemId)
                worn.Add($"{slot}:{_catalog.GetItem(itemId).Name}");
        }
        parts.Add(worn.Count > 0 ? $"装备：{string.Join("、", worn)}" : "装备：无");

        if (unit.Buffs.Count > 0)
            parts.Add($"Buff：{string.Join("、", unit.Buffs.Keys.Select(id => _catalog.GetBuff(id).Name))}");
        if (unit.Status != CombatStatus.None)
            parts.Add($"状态：{StatusText(unit.Status)}");
        return UiHelpers.Wrap(string.Join("\n", parts));
    }

    private void UpdateAllLabels()
    {
        var mine = _units[_mySide];
        var enemy = _units[_mySide == "A" ? "B" : "A"];

        _selfName.Text = $"我方：{mine.Name}";
        _selfHp.MaxValue = Math.Max(1, mine.MaxHp);
        _selfHp.Value = mine.Hp;
        _selfHpText.Text = $"{mine.Hp} / {mine.MaxHp}";
        _selfMp.MaxValue = Math.Max(1, mine.MaxMp);
        _selfMp.Value = mine.Mp;
        _selfMpText.Text = $"{mine.Mp} / {mine.MaxMp}";
        _selfPortrait.TooltipText = DynamicHeroTooltip(_mySide);
        _enemyPortrait.TooltipText = DynamicHeroTooltip(_mySide == "A" ? "B" : "A");

        var selfMeta = new List<string> { $"金币 {mine.Gold}" };
        var equip = new List<string>();
        foreach (var (slot, id) in _equipmentBySide[_mySide])
        {
            if (id is { } itemId)
                equip.Add($"{slot}:{_catalog.GetItem(itemId).Name}");
        }
        selfMeta.Add(equip.Count > 0 ? $"装备 {string.Join(" ", equip)}" : "无装备");
        if (mine.Status != CombatStatus.None)
            selfMeta.Add($"状态：{StatusText(mine.Status)}");
        _selfMeta.Text = string.Join(" ｜ ", selfMeta);

        _enemyName.Text = $"对方：{enemy.Name}";
        _enemyHp.MaxValue = Math.Max(1, enemy.MaxHp);
        _enemyHp.Value = enemy.Hp;
        _enemyHpText.Text = $"{enemy.Hp} / {enemy.MaxHp}";
        _enemyMp.MaxValue = Math.Max(1, enemy.MaxMp);
        _enemyMp.Value = enemy.Mp;
        _enemyMpText.Text = $"{enemy.Mp} / {enemy.MaxMp}";

        var enemyStatus = new List<string> { $"金币 {enemy.Gold}" };
        if (enemy.Status != CombatStatus.None)
            enemyStatus.Add($"状态：{StatusText(enemy.Status)}");
        _enemyStatus.Text = string.Join(" ｜ ", enemyStatus);

        if (_buffVersion > 0 || !_buffIconsBuilt)
        {
            RebuildBuffIcons();
            _buffIconsBuilt = true;
        }
    }

    private static string StatusText(CombatStatus status)
    {
        var parts = new List<string>();
        if (status.Has(CombatStatus.Incapacitated)) parts.Add("完全行动不能");
        if (status.Has(CombatStatus.Limited)) parts.Add("行动受限");
        if (status.Has(CombatStatus.Disarmed)) parts.Add("攻击不能");
        if (status.Has(CombatStatus.Silenced)) parts.Add("施法不能");
        if (status.Has(CombatStatus.Pacified)) parts.Add("战斗不能");
        return parts.Count > 0 ? string.Join("+", parts) : "";
    }

    private void SetActionsEnabled(bool enabled)
    {
        foreach (var button in new[] { _q, _w, _e, _r, _attack, _confirm })
            button.Disabled = !enabled;
    }

    private async Task SendCommand(string method, params object?[] args)
    {
        try
        {
            var allArgs = new object?[] { Net.Instance.Token }.Concat(args).ToArray();
            await Net.Instance.Battle!.InvokeCoreAsync(method, typeof(object), allArgs, CancellationToken.None);
        }
        catch (Exception ex)
        {
            AppendLog($"{method} 失败：{ex.Message}", "red");
        }
    }

    private void OnBackPressed() => GetTree().ChangeSceneToFile("res://scenes/MainMenu.tscn");

    private string SideName(BattleSide side) => side.ToString() == _mySide ? "我方" : "对方";

    private static string PhaseName(BattlePhase phase) => phase switch
    {
        BattlePhase.Warmup => "热身",
        BattlePhase.Shop => "商店",
        BattlePhase.Prepare => "励兵秣马",
        BattlePhase.Action => "运筹帷幄",
        BattlePhase.Resolving => "兵戎相见",
        BattlePhase.Ended => "结束",
        _ => phase.ToString(),
    };

    /// <summary>追加日志（BBCode 着色：red/green/gold/sky 等，默认白色）。</summary>
    private void AppendLog(string text, string color = "white")
    {
        _log.AppendText($"[color={color}]{text}[/color]\n");
        if (Net.Instance.IsBot)
            GD.Print($"[bot][{_mySide}] {text}");
    }
}
