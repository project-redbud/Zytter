using Zytter.Core.Buffs;
using Zytter.Core.Common;
using Zytter.Core.Data;
using Zytter.Core.Economy;
using Zytter.Core.Heroes;
using Zytter.Core.Rules;
using Zytter.Core.Skills;

namespace Zytter.Core.Battle;

/// <summary>对局内命令（客户端意图，服务器权威校验）。</summary>
public abstract record BattleCommand(BattleSide Side);

/// <summary>行动阶段提交行动。</summary>
public sealed record SubmitActionCommand(BattleSide Side, PlayerAction Action) : BattleCommand(Side);

/// <summary>商店购买。</summary>
public sealed record ShopPurchaseCommand(BattleSide Side, int ItemId) : BattleCommand(Side);

/// <summary>装备穿戴/脱下（itemId=0 表示脱下）。</summary>
public sealed record EquipCommand(BattleSide Side, EquipmentSlot Slot, int ItemId) : BattleCommand(Side);

/// <summary>准备阶段行动受限时的复苏选择。</summary>
public sealed record ReviveChoiceCommand(BattleSide Side, ReviveChoice Choice) : BattleCommand(Side);

public enum ReviveChoice
{
    /// <summary>使用复苏胶囊（id 5）。</summary>
    UseRevive,

    /// <summary>使用高级复苏胶囊（id 6）。</summary>
    UseRevivePlus,

    /// <summary>放弃使用，本轮行动受限保留。</summary>
    Cancel,
}

/// <summary>选择结晶之力分支（1/2/3）。</summary>
public sealed record CrystalChoiceCommand(BattleSide Side, int Branch) : BattleCommand(Side);

/// <summary>暂停/解除暂停。</summary>
public sealed record PauseCommand(BattleSide Side, bool Resume) : BattleCommand(Side);

/// <summary>投降（第 13 回合起可用）。</summary>
public sealed record SurrenderCommand(BattleSide Side) : BattleCommand(Side);

/// <summary>
/// 权威对局引擎：一场 1v1 对局的全部规则。
/// 客户端只提交 <see cref="BattleCommand"/>，引擎校验并推进状态、产出 <see cref="BattleEvent"/> 流。
/// 随机数全部经 <see cref="IRng"/>（可注入种子确定性重放）。
/// </summary>
public sealed class BattleSession
{
    // ==================== 依赖与静态数据 ====================

    public GameDataCatalog Catalog { get; }
    public BattleConfig Config { get; }
    public IRng Rng { get; }

    public BuffEffectRegistry BuffEffects { get; } = new();
    public SkillEffectRegistry SkillEffects { get; } = new();
    public ItemEffectRegistry ItemEffects { get; } = new();

    // ==================== 玩家与回合状态 ====================

    public BattlePlayer PlayerA { get; }
    public BattlePlayer PlayerB { get; }

    public BattlePlayer Player(BattleSide side) => side == BattleSide.A ? PlayerA : PlayerB;
    public BattlePlayer Opponent(BattleSide side) => Player(side.Opponent());

    public BattlePhase Phase { get; private set; } = BattlePhase.Warmup;
    public double PhaseRemainingSeconds { get; private set; }

    /// <summary>战斗回合 r（1 起，商店回合不计数）。</summary>
    public int Round { get; private set; }

    /// <summary>循环计数 nextround（含商店回合，1 起到 RoundLimit）。</summary>
    public int NextRound { get; private set; }

    /// <summary>回合上限（初始 35，延时道具 +5）。</summary>
    public int RoundLimit { get; private set; }

    /// <summary>回合结束标记 rend（每次结算后 +1，用于紫月/风之结界等延时机制）。</summary>
    public int Rend { get; private set; }

    /// <summary>商店成长等级（0~4，商店回合 6/13/20/27 各 +1）。</summary>
    public int GrowthLevel { get; private set; }

    public bool IsPaused { get; private set; }
    public BattleSide? PauseOwner { get; private set; }
    public double PauseRemainingSeconds { get; private set; }

    public bool IsFinished => Phase == BattlePhase.Ended;
    public BattleSide? Winner { get; private set; }
    public VictoryReason? WinReason { get; private set; }

    private readonly Dictionary<BattleSide, PlayerAction?> _pendingActions = new();
    private readonly List<BattleEvent> _log = new();
    private long _seq;
    private int _combatantSeq;

    /// <summary>紫月神杖延时伤害队列（每项：施放方 + 剩余 rend 计数，到期对敌方当前英雄结算）。</summary>
    private readonly List<ZiyuePending> _ziyuePendings = new();

    private sealed class ZiyuePending
    {
        public required BattleSide Side { get; init; }
        public int RendsRemaining { get; set; }
    }

    /// <summary>风之结界"行动受限"待生效队列（rend 到达时对目标生效）。</summary>
    private readonly List<WindBarrierPending> _windBarrierPendings = new();

    private sealed class WindBarrierPending
    {
        public required BattleSide VictimSide { get; init; }
        public int TriggerRend { get; init; }
    }

    /// <summary>新星冲刺（杨圣诺Q）护甲偷取待生效队列。</summary>
    private readonly List<StarRushPending> _starRushPendings = new();

    private sealed class StarRushPending
    {
        public required BattleSide CasterSide { get; init; }
        public int ApplyRend { get; init; }
        public int RestoreRend { get; init; }
        public double Stolen { get; set; }
    }

    public IReadOnlyList<BattleEvent> Log => _log;

    /// <summary>事件发布钩子：服务器订阅以实时广播；日志始终记录（供回放/断线补发）。</summary>
    public event Action<BattleEvent>? EventEmitted;

    public BattleSession(
        GameDataCatalog catalog,
        BattleConfig config,
        IRng rng,
        IReadOnlyList<HeroDefinition> rosterA,
        IReadOnlyList<HeroDefinition> rosterB)
    {
        if (rosterA.Count == 0 || rosterB.Count == 0)
            throw new GameDataException("双方英雄名单不能为空");
        if (rosterA.Count > 3 || rosterB.Count > 3)
            throw new GameDataException("英雄名单最多 3 名");

        Catalog = catalog;
        Config = config;
        Rng = rng;
        RoundLimit = config.InitialRoundLimit;

        PlayerA = new BattlePlayer { Side = BattleSide.A, Roster = rosterA, PausesLeft = config.PauseTimes };
        PlayerB = new BattlePlayer { Side = BattleSide.B, Roster = rosterB, PausesLeft = config.PauseTimes };

        PlayerA.Current = CreateCombatant(BattleSide.A, rosterA[0]);
        PlayerB.Current = CreateCombatant(BattleSide.B, rosterB[0]);

        // 注册效果管线：36 技能 + Buff + 物品
        Skills.SkillEffectsInstaller.Install(this);
        Buffs.BuffEffectsInstaller.Install(this);
        Items.ItemEffectsInstaller.Install(this);
    }

    // ==================== 事件流 ====================

    public long NextSeq() => ++_seq;

    public void Emit(BattleEvent e)
    {
        _log.Add(e);
        EventEmitted?.Invoke(e);
    }

    /// <summary>驱动 Buff 挂点：遍历宿主 Buff 容器中已注册的 Buff 效果（可指定单个实例）。</summary>
    public void ExecuteBuffs(BuffHook hook, BuffContext ctx, BuffInstance? only = null)
    {
        IEnumerable<BuffInstance> buffs = only is null ? ctx.Self.Buffs.All.ToList() : new[] { only };
        foreach (var buff in buffs)
        {
            BuffEffects.Get(buff.Definition.Id)?.Handle(hook, buff, ctx);
        }
    }

    /// <summary>统一 Buff 施加入口：应用实例 + 触发 Applied 挂点 + 广播事件。</summary>
    public BuffInstance ApplyBuff(Combatant target, Data.BuffDefinition definition, int stacks, int durationRounds, BuffApplyMode mode = BuffApplyMode.Refresh)
    {
        var buff = target.Buffs.Apply(definition, stacks, durationRounds, mode);
        ExecuteBuffs(BuffHook.Applied, new BuffContext { Session = this, Self = target }, buff);
        // 事件携带展示用剩余回合（控制/时光沙漏/神谕等无回合概念的 Buff 也给出正确倒计时，施加瞬间即正确）
        Emit(new BuffAppliedEvent(NextSeq(), target.Side, target.Id, definition.Id, definition.Name, buff.Stacks, BuffDisplayRounds(target, buff)));
        return buff;
    }

    /// <summary>统一 Buff 移除入口：移除实例 + 触发 Removed 挂点 + 广播事件。</summary>
    public bool RemoveBuff(Combatant target, string buffId)
    {
        var buff = target.Buffs.Get(buffId);
        if (buff is null || !target.Buffs.Remove(buffId)) return false;
        ExecuteBuffs(BuffHook.Removed, new BuffContext { Session = this, Self = target }, buff);
        Emit(new BuffRemovedEvent(NextSeq(), target.Side, target.Id, buffId, Catalog.GetBuff(buffId).Name));
        return true;
    }

    // ==================== 生命周期 ====================

    /// <summary>开始对局（热身 20 秒）。</summary>
    public void Start()
    {
        Emit(new BattleStartedEvent(NextSeq(), RoundLimit));
        SetPhase(BattlePhase.Warmup, Config.WarmupSeconds);
    }

    /// <summary>推进时间（服务器按固定节拍调用）。</summary>
    public void Tick(double deltaSeconds)
    {
        if (IsFinished) return;

        if (IsPaused)
        {
            PauseRemainingSeconds -= deltaSeconds;
            if (PauseRemainingSeconds <= 0)
                ResumePause(PauseOwner ?? BattleSide.A);
            return;
        }

        PhaseRemainingSeconds -= deltaSeconds;
        if (PhaseRemainingSeconds > 0) return;

        switch (Phase)
        {
            case BattlePhase.Warmup:
                NextRound = 1;
                ContinueRounds();
                break;
            case BattlePhase.Shop:
                CloseShop();
                break;
            case BattlePhase.Prepare:
                BeginActionPhase();
                break;
            case BattlePhase.Action:
                FinalizeActions();
                break;
            case BattlePhase.Resolving:
                EndRoundAndContinue();
                break;
        }
    }

    private void SetPhase(BattlePhase phase, double seconds)
    {
        Phase = phase;
        PhaseRemainingSeconds = seconds;
        Emit(new PhaseChangedEvent(NextSeq(), phase, (int)Math.Ceiling(seconds)));
    }

    /// <summary>进入下一轮循环：商店回合开商店，否则开新战斗回合。</summary>
    private void ContinueRounds()
    {
        if (IsFinished) return;

        if (Config.IsShopRound(NextRound))
        {
            OpenShop();
        }
        else
        {
            StartRound();
        }
    }

    private void StartRound()
    {
        Round++;
        foreach (var side in new[] { BattleSide.A, BattleSide.B })
        {
            var c = Player(side).Current;
            if (c is not null) c.HeroTime++;
        }

        // discountbuff：回合开始 Buff 倒计时与触发
        foreach (var side in new[] { BattleSide.A, BattleSide.B })
        {
            var combatant = Player(side).Current;
            if (combatant is null) continue;

            var expired = combatant.Buffs.TickTurnStart();
            foreach (var buff in expired)
            {
                Emit(new BuffRemovedEvent(NextSeq(), side, combatant.Id, buff.Definition.Id, buff.Definition.Name));
                ExecuteBuffs(BuffHook.Removed, new BuffContext { Session = this, Self = combatant }, buff);
            }
            ExecuteBuffs(BuffHook.TurnStart, new BuffContext { Session = this, Self = combatant });
        }

        Emit(new RoundStartedEvent(NextSeq(), Round, RoundLimit));
        EmitBuffSync();

        // 励兵秣马：任一方行动受限 → 10 秒复苏窗口，否则 3 秒
        bool anyLimited = PlayerA.Current?.Status.Has(CombatStatus.Limited) == true
                       || PlayerB.Current?.Status.Has(CombatStatus.Limited) == true;
        SetPhase(BattlePhase.Prepare, anyLimited ? Config.PrepareLimitedSeconds : Config.PrepareSeconds);
    }

    private void OpenShop()
    {
        // 商店成长仅在 6/13/20/27 四个回合触发一次（heroup 计数每回合 +1，
        // 双方共享；此前在双方循环里各自 +1 导致计数翻倍、20/27 成长被跳过）
        bool growThisRound = GrowthLevel < 4 && new[] { 6, 13, 20, 27 }.Contains(NextRound);

        foreach (var side in new[] { BattleSide.A, BattleSide.B })
        {
            var player = Player(side);
            int grant = NextRound < Config.InitialRoundLimit ? Config.ShopEarlyGold : Config.ShopLateGold;
            player.Wallet.Add(grant);
            Emit(new GoldChangedEvent(NextSeq(), side, player.Wallet.Gold, grant));
            int shopping = NextRound < Config.InitialRoundLimit ? Config.ShopSeconds : Config.ShopOvertimeSeconds;
            Emit(new ShopOpenedEvent(NextSeq(), side, grant, shopping));

            if (growThisRound)
                ApplyGrowth(player);
        }
        if (growThisRound)
            GrowthLevel++;
        SetPhase(BattlePhase.Shop, NextRound < Config.InitialRoundLimit ? Config.ShopSeconds : Config.ShopOvertimeSeconds);
    }

    /// <summary>商店成长：当前英雄 +生命上限/+5% 物理减免（heroup 由调用方统一 +1）。</summary>
    private void ApplyGrowth(BattlePlayer player)
    {
        int hpBonus = GrowthLevel switch { 0 => 4, 1 => 4, 2 => 5, 3 => 5, _ => 0 };
        player.Current?.Stats.AddMaxHp(hpBonus);
        player.Current?.Stats.AddPhysicalDamageReduction(0.05);
    }

    private void CloseShop()
    {
        NextRound++;
        ContinueRounds();
    }

    // ==================== 行动阶段 ====================

    private void BeginActionPhase()
    {
        _pendingActions.Clear();
        SetPhase(BattlePhase.Action, Config.ActionSeconds);

        // 完全行动不能/行动受限未解除 → 自动放弃本轮行动
        foreach (var side in new[] { BattleSide.A, BattleSide.B })
        {
            var c = Player(side).Current;
            if (c is null) continue;
            if (c.Status.Has(CombatStatus.Incapacitated) || c.Status.Has(CombatStatus.Limited))
            {
                _pendingActions[side] = new SkipAction();
                Emit(new ActionSkippedEvent(NextSeq(), side, "control_status"));
            }
        }

        // 双方均无法行动 → 立即结算（对应原版"双方都决定后行动阶段提前结束"）
        if (_pendingActions.Count == 2)
        {
            PhaseRemainingSeconds = 0;
            FinalizeActions();
        }
    }

    private void FinalizeActions()
    {
        foreach (var side in new[] { BattleSide.A, BattleSide.B })
        {
            if (!_pendingActions.ContainsKey(side))
            {
                _pendingActions[side] = new SkipAction();
                Emit(new ActionSkippedEvent(NextSeq(), side, "timeout"));
            }
        }
        ResolveRound();
    }

    private void ResolveRound()
    {
        SetPhase(BattlePhase.Resolving, Config.ResolveSeconds);

        // 1. 行动排序：先手特权层级 → 行动力 → 房主先手
        var order = new[] { BattleSide.A, BattleSide.B }
            .OrderByDescending(side => PriorityOf(side))
            .ThenByDescending(side => EffectiveActionPower(side))
            .ThenBy(side => side) // 平手房主（A）先
            .ToList();

        // 2. 依次执行（行动者已死亡则跳过其行动）
        foreach (var side in order)
        {
            if (IsFinished) return;
            var actor = Player(side).Current;
            if (actor is null || actor.IsDead)
            {
                Emit(new ActionSkippedEvent(NextSeq(), side, "dead"));
                continue;
            }
            ExecuteAction(side, _pendingActions[side] ?? new SkipAction());
        }

        // 3. 结算后机制：灼烧 → 时光沙漏倒计时 → 风之结界待生效 → 新星冲刺偷甲 → 结晶检查 → 紫月 → rend++
        BurnTicks();
        HourglassCountdowns();
        ProcessWindBarrierPendings();
        ProcessStarRushPendings();
        CrystalChecks();
        ProcessZiyuePendings();
        Rend++;
        ClearExpiredControls();
        ExpireLiberationStacks();

        // 4. 死亡处理（可能触发换人或终局）
        foreach (var side in new[] { BattleSide.A, BattleSide.B })
        {
            if (IsFinished) return;
            var combatant = Player(side).Current;
            if (combatant is not null && combatant.IsDead)
                HandleHeroDeath(side);
        }
    }

    private int PriorityOf(BattleSide side)
    {
        var action = _pendingActions.TryGetValue(side, out var a) ? a : null;
        if (action is not CastSkillAction cast) return 0;
        var combatant = Player(side).Current;
        if (combatant is null) return 0;
        var skill = combatant.GetSkill(cast.Slot);
        if (skill is null) return 0;
        var effect = SkillEffects.Get(skill.Definition.Effect);
        return effect.GetPriorityTier(new SkillCastContext
        {
            Session = this,
            Caster = combatant,
            Target = Opponent(side).Current,
            Slot = cast.Slot,
            Runtime = skill,
        });
    }

    private double EffectiveActionPower(BattleSide side) =>
        Player(side).Current is { } c ? StatsResolver.ActionPower(this, c) : 0;

    // ==================== 行动执行 ====================

    private void ExecuteAction(BattleSide side, PlayerAction action)
    {
        var combatant = Player(side).Current!;
        var enemy = Opponent(side).Current;

        switch (action)
        {
            case BasicAttackAction:
                if (enemy is not null)
                    DamageCalculator.BasicAttack(this, combatant, enemy);
                break;

            case CastSkillAction cast:
                CastSkill(side, combatant, cast.Slot, enemy, cast.ChainQ);
                break;

            case UseItemAction use:
                UseItem(side, combatant, use.ItemId);
                break;

            case SkipAction:
                Emit(new ActionSkippedEvent(NextSeq(), side, "give_up"));
                break;
        }
    }

    private void CastSkill(BattleSide side, Combatant caster, SkillSlot slot, Combatant? target, bool chainQ = false)
    {
        var runtime = caster.GetSkill(slot) ?? throw new RuleViolationException("skill_not_available");
        var effect = SkillEffects.Get(runtime.Definition.Effect);
        var ctx = new SkillCastContext { Session = this, Caster = caster, Target = target, Slot = slot, Runtime = runtime };
        effect.Validate(ctx);

        // 统一扣蓝（原版：结算时先扣蓝再执行效果）
        caster.Stats.AddMp(-runtime.Definition.Mp);
        Emit(new SkillCastEvent(NextSeq(), side, runtime.Definition.Id, runtime.Definition.Name, runtime.Definition.Mp));

        effect.Execute(ctx);

        // 星辰陨落追加 Q：扣 Q 耗蓝并执行 Q 效果
        if (chainQ)
        {
            var q = caster.GetSkill(SkillSlot.Q)!;
            caster.Stats.AddMp(-q.Definition.Mp);
            Emit(new SkillCastEvent(NextSeq(), side, q.Definition.Id, q.Definition.Name, q.Definition.Mp));
            var qCtx = new SkillCastContext { Session = this, Caster = caster, Target = target, Slot = SkillSlot.Q, Runtime = q };
            SkillEffects.Get(q.Definition.Effect).Execute(qCtx);
        }
    }

    private void UseItem(BattleSide side, Combatant user, int itemId)
    {
        var player = Player(side);
        var def = Catalog.GetItem(itemId);
        if (def.Kind != ItemKind.Consumable || itemId < 3 || itemId > 12)
            throw new RuleViolationException("item_not_usable", $"物品 {def.Name} 不能在战斗中使用");

        if (!player.Box.Contains(itemId))
            throw new RuleViolationException("item_not_found", $"道具盒中没有 {def.Name}");

        player.Box.Remove(itemId);
        EmitItemLost(side, itemId, "use");
        Emit(new ItemUsedEvent(NextSeq(), side, itemId, def.Name));

        var effect = ItemEffects.Get(def.Effect);
        if (effect is null)
            throw new GameDataException($"消耗品效果 {def.Effect} 未注册");
        effect.Use(new ItemContext { Session = this, User = user, ItemId = itemId });
    }

    // ==================== 结算后机制 ====================

    /// <summary>灼烧 tick：双方各自结算（受害者视角），伤害来源为对方当前英雄。</summary>
    private void BurnTicks()
    {
        foreach (var side in new[] { BattleSide.A, BattleSide.B })
        {
            var victim = Player(side).Current;
            if (victim is null) continue;
            double stacks = victim.State.GetValueOrDefault("burn_stacks");
            if (stacks <= 0) continue;

            victim.State["burn_stacks"] = stacks - 1;
            if (stacks - 1 <= 0)
                RemoveBuff(victim, "burn");

            var source = Opponent(side).Current;
            if (source is not null && !source.IsDead && !victim.IsDead)
            {
                // 伤害来源属性取对方（其 yyE 加成与魔穿）
                double bonus = source.GetSkill(SkillSlot.E)?.GetState("magic_bonus") ?? 0;
                int d = DamageCalculator.ComputeMagicDamage(this, source, victim, (int)(5 + bonus));

                // 洁净点 → 禁卫军 → 时光沙漏（原版灼烧链）
                var q = victim.Hero.Id == 7 ? victim.GetSkill(SkillSlot.Q) : null;
                if (q is not null)
                {
                    double purity = Math.Min(8, q.GetState("purity") + d);
                    q.SetState("purity", purity);
                    q.Definition = q.Definition with { Mp = (int)purity };
                }
                if (victim.GetSkill(SkillSlot.R)?.GetState("guards") is > 0)
                {
                    double guards = victim.GetSkill(SkillSlot.R)!.GetState("guards") - 1;
                    victim.GetSkill(SkillSlot.R)!.SetState("guards", guards);
                    d = Math.Max(0, d - Math.Min(4, d));
                    if (guards <= 0)
                        Emit(new BuffRemovedEvent(NextSeq(), side, victim.Id, "princess_order", "公主号令"));
                }
                if (victim.Buffs.Get("hourglass") is { } hourglass)
                {
                    hourglass.V1 += d;
                    d = 0;
                }

                if (d > 0)
                {
                    victim.Stats.AddHp(-d);
                    Emit(new DamageDealtEvent(NextSeq(), side, victim.Id, d, DamageType.Magical));
                }

                // 受害者自身的二阶红月/紫月被动触发（原版怪癖：灼烧承伤触发自身装备被动）
                if (victim.Equipment.IsWorn(27))
                    ProcRedMoon(victim, source);
                if (victim.Equipment.IsWorn(13))
                    ScheduleZiyue(victim, source);
            }
        }
    }

    /// <summary>时光沙漏倒计时：到期自然释放 160%，未累计伤害则破碎。</summary>
    private void HourglassCountdowns()
    {
        foreach (var side in new[] { BattleSide.A, BattleSide.B })
        {
            var holder = Player(side).Current;
            if (holder is null) continue;
            var buff = holder.Buffs.Get("hourglass");
            if (buff is null) continue;

            double rounds = holder.State.GetValueOrDefault("hourglass_rounds");
            rounds--;
            holder.State["hourglass_rounds"] = rounds;

            if (rounds > 0) continue;

            ReleaseHourglass(side, holder, buff, 1.6);
        }
    }

    /// <summary>释放时光沙漏：累计伤害 × 倍率 - 魔抗削减，走完整魔法链。</summary>
    private void ReleaseHourglass(BattleSide holderSide, Combatant holder, BuffInstance buff, double factor)
    {
        holder.Buffs.Remove("hourglass");
        holder.State.Remove("hourglass_rounds");
        Emit(new BuffRemovedEvent(NextSeq(), holderSide, holder.Id, "hourglass", "时光沙漏"));

        var enemy = Opponent(holderSide).Current;
        if (enemy is null || enemy.IsDead) return;

        if (buff.V1 > 0)
        {
            double app = StatsResolver.MagicPenetration(this, holder);
            double enemyAdf = StatsResolver.MagicDefense(this, enemy);
            int d = Math.Max(0, (int)Math.Round(buff.V1 * factor - ((1 - app) * enemyAdf)));
            DamageCalculator.Magic(this, holder, enemy, d, isEquipmentPassive: false);
        }
    }

    /// <summary>风之结界"行动受限"待生效队列。</summary>
    private void ProcessWindBarrierPendings()
    {
        foreach (var pending in _windBarrierPendings.ToList())
        {
            if (Rend + 1 < pending.TriggerRend) continue;
            _windBarrierPendings.Remove(pending);

            var victim = Player(pending.VictimSide).Current;
            if (victim is null || victim.IsDead) continue;

            TryApplyControl(victim, CombatStatus.Limited, "风之结界");

            // 解除施法者的"结界挂起"标记（挂起期间不可再放风之结界）
            var caster = Opponent(pending.VictimSide).Current;
            caster?.GetSkill(Data.SkillSlot.W)?.SetState("pending", 0);
        }
    }

    /// <summary>新星冲刺偷甲：r+1 结算末偷取 min(敌护甲,3)，r+2 结算末归还。</summary>
    public void ScheduleStarRush(BattleSide casterSide)
    {
        _starRushPendings.Add(new StarRushPending
        {
            CasterSide = casterSide,
            ApplyRend = Rend + 2,
            RestoreRend = Rend + 3,
        });
    }

    /// <summary>风之结界 70% 追加的行动受限：完全行动不能后的下一个回合结算末生效。</summary>
    public void ScheduleWindBarrier(BattleSide victimSide)
    {
        _windBarrierPendings.Add(new WindBarrierPending
        {
            VictimSide = victimSide,
            TriggerRend = Rend + 3,
        });
    }

    private void ProcessStarRushPendings()
    {
        foreach (var pending in _starRushPendings.ToList())
        {
            var caster = Player(pending.CasterSide).Current;
            var victim = Opponent(pending.CasterSide).Current;

            if (Rend + 1 >= pending.ApplyRend && pending.Stolen == 0)
            {
                if (caster is not null && !caster.IsDead && victim is not null && !victim.IsDead)
                {
                    double stolen = Math.Min(StatsResolver.Defense(this, victim), 3);
                    if (stolen > 0)
                    {
                        caster.Stats.AddDefense((int)stolen);
                        victim.Stats.AddDefense(-(int)stolen);
                        pending.Stolen = stolen;
                        var stealDef = Catalog.GetBuff("star_rush_steal");
                        caster.Buffs.Apply(stealDef, 1, -1);
                        Emit(new BuffAppliedEvent(NextSeq(), caster.Side, caster.Id, stealDef.Id, stealDef.Name, 1, -1));
                        var stolenDef = Catalog.GetBuff("star_rush_stolen");
                        victim.Buffs.Apply(stolenDef, 1, -1);
                        Emit(new BuffAppliedEvent(NextSeq(), victim.Side, victim.Id, stolenDef.Id, stolenDef.Name, 1, -1));
                    }
                    else
                    {
                        _starRushPendings.Remove(pending);
                    }
                }
                else
                {
                    _starRushPendings.Remove(pending);
                }
            }
            else if (pending.Stolen > 0 && Rend + 1 >= pending.RestoreRend)
            {
                if (caster is not null && !caster.IsDead)
                {
                    caster.Stats.AddDefense(-(int)pending.Stolen);
                    caster.Buffs.Remove("star_rush_steal");
                }
                if (victim is not null && !victim.IsDead)
                {
                    victim.Stats.AddDefense((int)pending.Stolen);
                    victim.Buffs.Remove("star_rush_stolen");
                }
                _starRushPendings.Remove(pending);
            }
        }
    }

    /// <summary>解放真名逐层到期扣回（每层独立 10 回合）。</summary>
    private void ExpireLiberationStacks()
    {
        foreach (var side in new[] { BattleSide.A, BattleSide.B })
        {
            var combatant = Player(side).Current;
            if (combatant is null) continue;
            var buff = combatant.Buffs.Get("liberation");
            if (buff is null) continue;

            int expired = buff.StackExpiryRends.RemoveAll(r => r <= Rend);
            if (expired <= 0) continue;

            buff.Stacks -= expired;
            if (buff.Stacks <= 0)
                RemoveBuff(combatant, "liberation");
        }
    }

    /// <summary>清除到期控制状态（原版"持续至 rend==r+2"语义：施加于 r 回合结算，r+1 回合末解除）。</summary>
    private void ClearExpiredControls()
    {
        foreach (var side in new[] { BattleSide.A, BattleSide.B })
        {
            var combatant = Player(side).Current;
            if (combatant is null || combatant.Status == CombatStatus.None) continue;

            if (combatant.State.TryGetValue("control_until_rend", out var until) && until <= Rend)
            {
                combatant.Status = CombatStatus.None;
                combatant.State.Remove("control_until_rend");
                foreach (var buffId in new[] { "wind_barrier_stun", "wind_barrier_lim", "ice_cross", "round_square", "rift" })
                {
                    if (combatant.Buffs.Remove(buffId))
                        Emit(new BuffRemovedEvent(NextSeq(), side, combatant.Id, buffId, buffId));
                }
                Emit(new StatusChangedEvent(NextSeq(), side, combatant.Id, combatant.Status));
            }
        }
    }

    /// <summary>结晶之力激活检查（herotime>=7 或累计伤害>=20）。</summary>
    private void CrystalChecks()
    {
        foreach (var side in new[] { BattleSide.A, BattleSide.B })
        {
            var c = Player(side).Current;
            if (c is null || !c.Hero.Crystal || c.CrystalActive) continue;
            if (c.HeroTime >= 7 || c.DamageDealt >= 20)
                Emit(new CrystalReadyEvent(NextSeq(), side));
        }
    }

    /// <summary>紫月神杖延时伤害：rend 到期对敌方当前英雄结算。</summary>
    private void ProcessZiyuePendings()
    {
        foreach (var pending in _ziyuePendings.ToList())
        {
            pending.RendsRemaining--;
            if (pending.RendsRemaining > 0) continue;
            _ziyuePendings.Remove(pending);

            var attacker = Player(pending.Side).Current;
            var defender = Opponent(pending.Side).Current;
            if (attacker is null || attacker.IsDead || defender is null || defender.IsDead) continue;

            int d = DamageCalculator.ComputeMagicDamage(this, attacker, defender, 4);
            DamageCalculator.Magic(this, attacker, defender, d, isEquipmentPassive: true);
        }
    }

    /// <summary>二阶红月神杖被动：每次造成伤害附加 round(目标最大HP*14%) - 魔抗削减 的魔法伤害（balancezy 链）。</summary>
    public void ProcRedMoon(Combatant attacker, Combatant defender)
    {
        int d = DamageCalculator.ComputeMagicDamage(this, attacker, defender, (int)Math.Round(defender.Stats.MaxHp * 0.14));
        DamageCalculator.Magic(this, attacker, defender, d, isEquipmentPassive: true);
    }

    /// <summary>紫月神杖被动：施放后第 1、2 回合结束时各造成 4 点魔法伤害（装备被动链）。</summary>
    public void ScheduleZiyue(Combatant attacker, Combatant defender)
    {
        _ziyuePendings.Add(new ZiyuePending { Side = attacker.Side, RendsRemaining = 1 });
        _ziyuePendings.Add(new ZiyuePending { Side = attacker.Side, RendsRemaining = 2 });
    }

    /// <summary>掠夺机制：每累计 15 点物理伤害掠夺对方 1 金币，跨 30 一次性掠夺 2 金币。</summary>
    public void AccumulatePlunder(Combatant attacker, Combatant defender, int physicalDamage)
    {
        if (physicalDamage <= 0) return;

        var attackerPlayer = Player(attacker.Side);
        var defenderPlayer = Player(defender.Side);

        attackerPlayer.PhysicalDamageDealt += physicalDamage;
        defenderPlayer.PhysicalDamageTaken += physicalDamage;

        if (attackerPlayer.PhysicalDamageDealt > 30)
        {
            attackerPlayer.PhysicalDamageDealt -= 30;
            TransferPlunder(defenderPlayer, attackerPlayer, 2);
        }
        else if (attackerPlayer.PhysicalDamageDealt > 15)
        {
            attackerPlayer.PhysicalDamageDealt -= 15;
            TransferPlunder(defenderPlayer, attackerPlayer, 1);
        }

        // 被掠夺方（robh 视角）
        if (defenderPlayer.PhysicalDamageTaken > 30)
        {
            defenderPlayer.PhysicalDamageTaken -= 30;
            TransferPlunder(defenderPlayer, attackerPlayer, 2);
        }
        else if (defenderPlayer.PhysicalDamageTaken > 15)
        {
            defenderPlayer.PhysicalDamageTaken -= 15;
            TransferPlunder(defenderPlayer, attackerPlayer, 1);
        }
    }

    private void TransferPlunder(BattlePlayer from, BattlePlayer to, int amount)
    {
        // 原版允许金币被掠夺至负数（gold--），如实保留
        if (from.Wallet.Gold >= amount)
            from.Wallet.Spend(amount);
        else
            from.Wallet.Spend(from.Wallet.Gold);
        to.Wallet.Add(amount);
        Emit(new GoldChangedEvent(NextSeq(), from.Side, from.Wallet.Gold, -amount));
        Emit(new GoldChangedEvent(NextSeq(), to.Side, to.Wallet.Gold, amount));
    }

    // ==================== 死亡与换人 ====================

    private void HandleHeroDeath(BattleSide side)
    {
        var player = Player(side);
        var killer = Opponent(side);
        var dead = player.Current!;

        Emit(new HeroDiedEvent(NextSeq(), side, dead.Id, dead.Hero.Name));
        player.Deaths++;
        killer.Kills++;
        killer.Wallet.Add(Config.KillGold);
        Emit(new GoldChangedEvent(NextSeq(), killer.Side, killer.Wallet.Gold, Config.KillGold));

        // 装备脱下回道具盒
        foreach (var slot in new[] { EquipmentSlot.Z, EquipmentSlot.X })
        {
            var worn = dead.Equipment.Remove(slot);
            if (worn is { } itemId)
            {
                player.Box.Add(itemId);
                Emit(new EquipmentChangedEvent(NextSeq(), side, slot.ToString(), null));
                EmitItemObtained(side, itemId, "death_return");
            }
        }

        if (!player.HasNextHero)
        {
            Finish(killer.Side, VictoryReason.Annihilation);
            return;
        }

        // 换人：先取下一名英雄，再推进下标（NextHero 定义为 Roster[RosterIndex + 1]，
        // 顺序颠倒会跳过英雄；此前的实现曾导致第一名阵亡直接换上第三名）
        var next = player.NextHero;
        if (next is null)
        {
            Finish(killer.Side, VictoryReason.Annihilation);
            return;
        }
        player.RosterIndex++;
        player.Current = CreateCombatant(side, next);
        Emit(new HeroSwitchedEvent(NextSeq(), side, next.Name, player.Current.Stats.MaxHp, player.Current.Stats.MaxMp));
        // 立即广播权威数值，确保客户端即使漏掉切换事件也能在当回合收敛到正确显示
        EmitHeroStatsSync(side);
    }

    private Combatant CreateCombatant(BattleSide side, HeroDefinition hero)
    {
        var stats = hero.CreateStats();

        // 商店成长补足：1→+4/+5%，2→+8/+10%，3→+13/+15%，4→+18/+20%
        (int hpBonus, double drBonus) = GrowthLevel switch
        {
            1 => (4, 0.05),
            2 => (8, 0.10),
            3 => (13, 0.15),
            4 => (18, 0.20),
            _ => (0, 0.0),
        };
        stats.AddMaxHp(hpBonus);
        // 补足同时回满当前生命：新英雄满血上场（旧版只加上限不加血，
        // 导致换人后 HP 停留在基础值、客户端一直显示残血）
        stats.AddHp(hpBonus);
        stats.AddPhysicalDamageReduction(drBonus);

        var skills = new Dictionary<SkillSlot, SkillRuntime>();
        foreach (var slot in new[] { SkillSlot.Q, SkillSlot.W, SkillSlot.E, SkillSlot.R })
        {
            var def = Catalog.GetSkill(hero, slot);
            if (def is not null)
                skills[slot] = new SkillRuntime(hero.Id, def);
        }

        return new Combatant(++_combatantSeq, side, hero, stats, skills, Catalog);
    }

    // ==================== 回合结束（偃革倒戈） ====================

    private void EndRoundAndContinue()
    {
        if (IsFinished) return;

        foreach (var side in new[] { BattleSide.A, BattleSide.B })
        {
            var player = Player(side);
            var combatant = player.Current;

            // 金币 +1
            player.Wallet.Add(Config.RoundEndGold);
            Emit(new GoldChangedEvent(NextSeq(), side, player.Wallet.Gold, Config.RoundEndGold));

            if (combatant is null || combatant.IsDead) continue;

            // 生命回复：hpp（装备/技能提供）
            double hpp = StatsResolver.HpRegen(this, combatant);
            if (hpp > 0)
            {
                int actual = combatant.Stats.AddHp((int)Math.Round(hpp));
                if (actual > 0)
                    Emit(new HealedEvent(NextSeq(), side, combatant.Id, actual));
            }

            // 洁净之灵（单数回合施放 Q 后的回合末回复）
            var q = combatant.Hero.Id == 7 ? combatant.GetSkill(SkillSlot.Q) : null;
            if (q is not null && q.GetState("heal_pending") is > 0 and var pendingHeal)
            {
                q.SetState("heal_pending", 0);
                int actual = combatant.Stats.AddHp((int)pendingHeal);
                if (actual > 0)
                    Emit(new HealedEvent(NextSeq(), side, combatant.Id, actual));
            }

            // 低血保底：HP ≤ 30% 最大生命 → + round(最大生命 * 7%)
            if (combatant.Stats.Hp <= combatant.Stats.MaxHp * Config.LowHpThreshold)
            {
                int heal = (int)Math.Round(combatant.Stats.MaxHp * Config.LowHpHealRatio);
                int actual = combatant.Stats.AddHp(heal);
                if (actual > 0)
                    Emit(new HealedEvent(NextSeq(), side, combatant.Id, actual));
            }

            // 魔法回复（除非被重伤/月光剑/予恋之花封锁）
            if (!combatant.MpRegenBlocked && !player.MpRegenBlocked)
            {
                double mpp = StatsResolver.MpRegen(this, combatant);
                if (mpp > 0)
                {
                    int actual = combatant.Stats.AddMp((int)Math.Round(mpp));
                    if (actual != 0)
                        Emit(new MpChangedEvent(NextSeq(), side, combatant.Id, actual));
                }
            }
            combatant.MpRegenBlocked = false;
            player.MpRegenBlocked = false;

            // 回合结束 Buff 挂点（紫月延时等已由 ProcessZiyuePendings 处理）
            ExecuteBuffs(BuffHook.TurnEnd, new BuffContext { Session = this, Self = combatant });

            // 破军之矛冷却递减
            if (combatant.State.GetValueOrDefault("pojun_cd") > 0)
                combatant.State["pojun_cd"]--;
        }

        // 权威属性同步：每回合结束广播双方当前英雄数值（客户端显示以服务器为准）
        EmitHeroStatsSync(BattleSide.A);
        EmitHeroStatsSync(BattleSide.B);

        Emit(new RoundEndedEvent(NextSeq(), Round, NextRound + 1));

        NextRound++;
        if (NextRound > RoundLimit)
        {
            Finish(VictoryJudge.JudgeByRoundExhaustion(PlayerA, PlayerB), VictoryReason.RoundExhausted);
            return;
        }

        ContinueRounds();
    }

    // ==================== 命令处理 ====================

    /// <summary>处理客户端命令。非法命令抛 <see cref="RuleViolationException"/>。</summary>
    public void Execute(BattleCommand command)
    {
        if (IsFinished)
            throw new RuleViolationException("battle_finished");

        switch (command)
        {
            case SubmitActionCommand submit:
                SubmitAction(submit.Side, submit.Action);
                break;
            case ShopPurchaseCommand purchase:
                ShopPurchase(purchase.Side, purchase.ItemId);
                break;
            case EquipCommand equip:
                Equip(equip.Side, equip.Slot, equip.ItemId);
                break;
            case ReviveChoiceCommand revive:
                HandleReviveChoice(revive.Side, revive.Choice);
                break;
            case CrystalChoiceCommand crystal:
                ChooseCrystal(crystal.Side, crystal.Branch);
                break;
            case PauseCommand pause:
                if (pause.Resume) ResumePause(pause.Side);
                else RequestPause(pause.Side);
                break;
            case SurrenderCommand surrender:
                Surrender(surrender.Side);
                break;
            default:
                throw new RuleViolationException("unknown_command");
        }
    }

    /// <summary>该方是否已在当前行动阶段锁定行动。供服务器侧 AI 驱动使用。</summary>
    public bool HasPendingAction(BattleSide side) => _pendingActions.ContainsKey(side);

    private void SubmitAction(BattleSide side, PlayerAction action)
    {
        if (Phase != BattlePhase.Action)
            throw new RuleViolationException("not_action_phase");
        if (_pendingActions.ContainsKey(side))
            throw new RuleViolationException("action_already_locked");
        if (IsPaused)
            throw new RuleViolationException("paused");

        var combatant = Player(side).Current
            ?? throw new RuleViolationException("no_combatant");

        // 战斗不能 → 不可决定任何行动；受限/完全行动不能已自动跳过
        if (combatant.Status.Has(CombatStatus.Pacified) && action is not SkipAction)
            throw new RuleViolationException("pacified");
        if (combatant.Status.Has(CombatStatus.Incapacitated) || combatant.Status.Has(CombatStatus.Limited))
            throw new RuleViolationException("controlled");

        switch (action)
        {
            case BasicAttackAction:
                if (!combatant.Status.CanBasicAttack())
                    throw new RuleViolationException("disarmed");
                break;

            case CastSkillAction cast:
                if (!combatant.Status.CanCast())
                    throw new RuleViolationException("silenced");
                var runtime = combatant.GetSkill(cast.Slot)
                    ?? throw new RuleViolationException("skill_not_available");
                int mpCost = runtime.Definition.Mp;
                if (cast.ChainQ)
                {
                    // 星辰陨落追加 Q：需额外 Q 耗蓝
                    var q = combatant.GetSkill(SkillSlot.Q)
                        ?? throw new RuleViolationException("chain_q_not_available");
                    if (runtime.Definition.Effect != "ysn_w")
                        throw new RuleViolationException("chain_q_only_for_ysn_w");
                    mpCost += q.Definition.Mp;
                }
                if (combatant.Stats.Mp < mpCost)
                    throw new RuleViolationException("not_enough_mp");
                var effect = SkillEffects.Get(runtime.Definition.Effect);
                effect.Validate(new SkillCastContext
                {
                    Session = this,
                    Caster = combatant,
                    Target = Opponent(side).Current,
                    Slot = cast.Slot,
                    Runtime = runtime,
                });
                break;

            case UseItemAction use:
                var def = Catalog.GetItem(use.ItemId);
                if (def.Kind != ItemKind.Consumable || use.ItemId < 3 || use.ItemId > 12)
                    throw new RuleViolationException("item_not_usable");
                if (!Player(side).Box.Contains(use.ItemId))
                    throw new RuleViolationException("item_not_found");
                break;
        }

        _pendingActions[side] = action;
        Emit(new ActionLockedEvent(NextSeq(), side));

        // 神谕规则校验（维多利娜Q：对方下一回合必须遵守，违反永久 -1 护甲）
        CheckOracle(side, action);

        // 双方都已锁定 → 行动阶段提前结束
        if (_pendingActions.Count == 2)
        {
            PhaseRemainingSeconds = 0;
            FinalizeActions();
        }
    }

    /// <summary>
    /// 神谕校验。修复原版缺陷：ww==2（必须用技能）判定恒为 false 导致永不惩罚。
    /// 规则：1=必须普攻、2=必须用技能、3=必须放弃；违规永久损失 1 点护甲。
    /// </summary>
    private void CheckOracle(BattleSide side, PlayerAction action)
    {
        var combatant = Player(side).Current;
        if (combatant is null) return;
        double rule = combatant.State.GetValueOrDefault("oracle_rule");
        if (rule <= 0) return;

        bool violated = rule switch
        {
            1 => action is not BasicAttackAction,
            2 => action is not CastSkillAction,
            3 => action is not SkipAction,
            _ => false,
        };

        combatant.State.Remove("oracle_rule");
        if (combatant.Buffs.Remove("oracle"))
            Emit(new BuffRemovedEvent(NextSeq(), side, combatant.Id, "oracle", "神谕"));
        EmitSkillInfo(side, "oracle", 0);

        if (violated)
        {
            combatant.Stats.AddDefense(-2); // 违反神谕：永久损失 2 点护甲
            Emit(new DamageDealtEvent(NextSeq(), side, combatant.Id, 0, DamageType.True));
            EmitSkillInfo(side, "oracle_result", 1); // 1=违反
        }
        else
        {
            EmitSkillInfo(side, "oracle_result", 0); // 0=遵守
        }
    }

    // ==================== 商店 ====================

    private void ShopPurchase(BattleSide side, int itemId)
    {
        if (Phase != BattlePhase.Shop)
            throw new RuleViolationException("not_shop_phase");

        var player = Player(side);
        var combatant = player.Current;
        var def = Catalog.GetItem(itemId);
        bool overtime = NextRound >= Config.InitialRoundLimit;

        // 加时赛不再出售回复类道具
        if (overtime && new[] { 3, 4, 7, 8, 9 }.Contains(itemId))
            throw new RuleViolationException("overtime_no_recovery");

        switch (itemId)
        {
            case 1: // 回合延时：+5 回合，双方同步，每玩家限购 1
                if (player.RoundExtendPurchased)
                    throw new RuleViolationException("round_extend_purchased");
                player.Wallet.Spend(def.Gold);
                player.RoundExtendPurchased = true;
                RoundLimit += Config.RoundExtendAmount;
                Emit(new BattleStartedEvent(NextSeq(), RoundLimit));
                break;

            case 2: // 回复药：即时 +2 生命，限购 3
                if (player.SmallPotionPurchased >= 3)
                    throw new RuleViolationException("small_potion_limit");
                player.Wallet.Spend(def.Gold);
                player.SmallPotionPurchased++;
                if (combatant is not null && !combatant.IsDead)
                {
                    int actual = combatant.Stats.AddHp(2);
                    if (actual > 0)
                        Emit(new HealedEvent(NextSeq(), side, combatant.Id, actual));
                }
                break;

            default:
                // 装备：红月神杖升级逻辑 + 加时赛吃装备逻辑
                if (def.Kind == ItemKind.Equipment)
                {
                    bool wornOrBoxed = combatant?.Equipment.IsWorn(itemId) == true || player.Box.Contains(itemId);

                    if (itemId == 14 && (wornOrBoxed || player.Box.Contains(27) || combatant?.Equipment.IsWorn(27) == true))
                    {
                        // 红月神杖：已拥有则升级为二阶
                        if (player.Box.Contains(27) || combatant?.Equipment.IsWorn(27) == true)
                            throw new RuleViolationException("hongyue_already_upgraded");
                        player.Wallet.Spend(def.Gold);
                        RemoveOwnedItem(player, combatant, 14);
                        player.Box.Add(27);
                        EmitItemObtained(side, 27, "purchase");
                        break;
                    }

                    if (overtime && wornOrBoxed && !player.HasEatenEquipment)
                    {
                        // 吃装备：永久获得装备属性，全场一次
                        player.Wallet.Spend(def.Gold);
                        player.HasEatenEquipment = true;
                        RemoveOwnedItem(player, combatant, itemId);
                        combatant?.Equipment.Consumed.Add(itemId);
                        break;
                    }

                    player.Wallet.Spend(def.Gold);
                    player.Box.Add(itemId);
                    EmitItemObtained(side, itemId, "purchase");
                    break;
                }

                // 消耗品入盒（容量 30）
                player.Wallet.Spend(def.Gold);
                player.Box.Add(itemId);
                EmitItemObtained(side, itemId, "purchase");
                break;
        }

        Emit(new GoldChangedEvent(NextSeq(), side, player.Wallet.Gold, -def.Gold));
    }

    /// <summary>
    /// 移除玩家拥有的某件装备（穿戴中则脱下并广播事件，在道具盒则取出）。
    /// 用于红月神杖升级（卸下原红月）与加时赛"吃装备"（消耗重复装备）。
    /// </summary>
    private void RemoveOwnedItem(BattlePlayer player, Combatant? combatant, int itemId)
    {
        if (combatant?.Equipment.IsWorn(itemId) == true)
        {
            foreach (var slot in new[] { EquipmentSlot.Z, EquipmentSlot.X })
            {
                if (combatant.Equipment.GetWorn(slot) == itemId)
                {
                    combatant.Equipment.Remove(slot);
                    Emit(new EquipmentChangedEvent(NextSeq(), player.Side, slot.ToString(), null));
                    EmitItemLost(player.Side, itemId, "upgrade_consume");
                }
            }
        }
        else if (player.Box.Contains(itemId))
        {
            player.Box.Remove(itemId);
            EmitItemLost(player.Side, itemId, "upgrade_consume");
        }
    }

    // ==================== 装备 ====================

    private void Equip(BattleSide side, EquipmentSlot slot, int itemId)
    {
        // 原版仅行动阶段可穿戴/脱卸
        if (Phase != BattlePhase.Action)
            throw new RuleViolationException("not_action_phase");

        var player = Player(side);
        var combatant = player.Current
            ?? throw new RuleViolationException("no_combatant");

        if (itemId == 0)
        {
            var removed = combatant.Equipment.Remove(slot);
            if (removed is { } id)
            {
                player.Box.Add(id);
                Emit(new EquipmentChangedEvent(NextSeq(), side, slot.ToString(), null));
                EmitItemObtained(side, id, "unequip");
            }
            return;
        }

        var def = Catalog.GetItem(itemId);
        if (def.Kind != ItemKind.Equipment)
            throw new RuleViolationException("item_not_equipment");
        if (!player.Box.Contains(itemId))
            throw new RuleViolationException("item_not_found");

        player.Box.Remove(itemId);
        EmitItemLost(side, itemId, "equip");
        var replaced = combatant.Equipment.Wear(slot, itemId);
        Emit(new EquipmentChangedEvent(NextSeq(), side, slot.ToString(), itemId));
        if (replaced is { } old)
        {
            player.Box.Add(old);
            EmitItemObtained(side, old, "unequip");
        }
    }

    private void EmitItemObtained(BattleSide side, int itemId, string source)
    {
        var def = Catalog.GetItem(itemId);
        Emit(new ItemObtainedEvent(NextSeq(), side, itemId, def.Name, source));
    }

    private void EmitItemLost(BattleSide side, int itemId, string reason)
    {
        var def = Catalog.GetItem(itemId);
        Emit(new ItemLostEvent(NextSeq(), side, itemId, def.Name, reason));
    }

    /// <summary>广播一侧当前英雄的权威数值（回合结束同步，含有效战斗属性）。</summary>
    private void EmitHeroStatsSync(BattleSide side)
    {
        var combatant = Player(side).Current;
        if (combatant is null) return;
        Emit(new HeroStatsSyncEvent(
            NextSeq(), side, combatant.Id, combatant.Hero.Name, combatant.Hero.Id,
            combatant.Stats.Hp, combatant.Stats.MaxHp, combatant.Stats.Mp, combatant.Stats.MaxMp,
            Rules.StatsResolver.Attack(this, combatant),
            Rules.StatsResolver.Defense(this, combatant),
            Rules.StatsResolver.MagicDefense(this, combatant),
            Rules.StatsResolver.ActionPower(this, combatant)));
    }

    /// <summary>广播技能状态信息（洁净点/魔王怒概率/神谕规则等，供客户端 tooltip 展示）。</summary>
    public void EmitSkillInfo(BattleSide side, string key, int value) =>
        Emit(new SkillInfoEvent(NextSeq(), side, key, value));

    /// <summary>每回合开始广播双方当前 Buff 的剩余持续回合（服务器权威，客户端不自行计算）。</summary>
    private void EmitBuffSync()
    {
        foreach (var side in new[] { BattleSide.A, BattleSide.B })
        {
            var combatant = Player(side).Current;
            if (combatant is null) continue;

            var rounds = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var buff in combatant.Buffs.All.OrderBy(b => b.Definition.Id, StringComparer.Ordinal))
                rounds[buff.Definition.Id] = BuffDisplayRounds(combatant, buff);

            Emit(new BuffSyncEvent(NextSeq(), side, combatant.Id, rounds));
        }
    }

    /// <summary>
    /// 计算某 Buff 的展示剩余回合。有回合概念的直接用 RemainingRounds；
    /// 控制类（风之结界/天圆地方/冰雪十字/裂缝）与控制时长达标的时光沙漏/神谕
    /// 由各自状态机给出剩余值；永久/层数型返回 -1（由其他机制展示）。
    /// </summary>
    private int BuffDisplayRounds(Combatant combatant, BuffInstance buff)
    {
        if (buff.RemainingRounds >= 0)
            return buff.RemainingRounds;

        return buff.Definition.Id switch
        {
            "wind_barrier_stun" or "wind_barrier_lim" or "ice_cross" or "round_square" or "rift" =>
                combatant.State.TryGetValue("control_until_rend", out var until)
                    ? Math.Max(1, (int)until - Rend)
                    : 1,
            "hourglass" =>
                combatant.State.TryGetValue("hourglass_rounds", out var hr)
                    ? Math.Max(1, (int)hr)
                    : 1,
            "oracle" => 1,
            _ => -1,
        };
    }

    // ==================== 复苏 ====================

    private void HandleReviveChoice(BattleSide side, ReviveChoice choice)
    {
        if (Phase != BattlePhase.Prepare)
            throw new RuleViolationException("not_prepare_phase");

        var player = Player(side);
        var combatant = player.Current;
        if (combatant is null || !combatant.Status.Has(CombatStatus.Limited))
            throw new RuleViolationException("not_limited");

        switch (choice)
        {
            case ReviveChoice.Cancel:
                return; // 保留行动受限，行动阶段自动跳过

            case ReviveChoice.UseRevive:
                UseReviveItem(side, 5);
                break;

            case ReviveChoice.UseRevivePlus:
                UseReviveItem(side, 6);
                break;
        }
    }

    /// <summary>使用复苏胶囊（id 5）/高级复苏胶囊（id 6）：解除控制 + 3 回合控制免疫（高级额外 +5 生命）。</summary>
    private void UseReviveItem(BattleSide side, int itemId)
    {
        var player = Player(side);
        var combatant = player.Current!;
        if (!player.Box.Contains(itemId))
            throw new RuleViolationException("item_not_found");

        player.Box.Remove(itemId);
        EmitItemLost(side, itemId, "use");
        Emit(new ItemUsedEvent(NextSeq(), side, itemId, Catalog.GetItem(itemId).Name));

        if (itemId == 6)
        {
            int actual = combatant.Stats.AddHp(5);
            if (actual > 0)
                Emit(new HealedEvent(NextSeq(), side, combatant.Id, actual));
        }

        ClearControlStatus(combatant);
        ApplyRevival(combatant);
    }

    /// <summary>解除全部控制状态并移除控制类 Buff。</summary>
    public void ClearControlStatus(Combatant combatant)
    {
        combatant.Status = CombatStatus.None;
        foreach (var buffId in new[] { "wind_barrier_stun", "wind_barrier_lim", "ice_cross", "round_square", "rift", "love_flower_enemy", "oracle" })
        {
            if (combatant.Buffs.Remove(buffId))
                Emit(new BuffRemovedEvent(NextSeq(), combatant.Side, combatant.Id, buffId, buffId));
        }
        combatant.State.Remove("limited_until_rend");
        Emit(new StatusChangedEvent(NextSeq(), combatant.Side, combatant.Id, combatant.Status));
    }

    /// <summary>施加 3 回合复苏（控制免疫）。</summary>
    public void ApplyRevival(Combatant combatant)
    {
        ApplyBuff(combatant, Catalog.GetBuff("revival"), 1, 3, BuffApplyMode.Refresh);
    }

    /// <summary>
    /// 尝试对目标施加强控制（完全行动不能/行动受限/战斗不能/施法不能）。
    /// 夜宴之声（消耗 1 次）与复苏（3 回合免疫）可抵挡；完全行动不能/行动受限/战斗不能会打断时光沙漏（0.8 倍释放）。
    /// 返回是否真正施加（被抵挡时返回 false，调用方据此决定是否挂控制类展示 Buff）。
    /// </summary>
    public bool TryApplyControl(Combatant victim, CombatStatus control, string sourceName)
    {
        if (victim.Buffs.Has("revival"))
        {
            Emit(new ActionSkippedEvent(NextSeq(), victim.Side, $"control_blocked_by_revival:{sourceName}"));
            return false;
        }

        // 夜宴之声：抵挡一次，次数耗尽自动失效
        if (victim.Equipment.IsWorn(26) && Player(victim.Side).NightBanquetUses < 3)
        {
            Player(victim.Side).NightBanquetUses++;
            Emit(new ActionSkippedEvent(NextSeq(), victim.Side, $"control_blocked_by_banquet:{sourceName}"));
            if (Player(victim.Side).NightBanquetUses >= 3)
                RemoveNightBanquet(victim);
            return false;
        }

        victim.Status |= control;
        victim.State["control_until_rend"] = Rend + 2;
        Emit(new StatusChangedEvent(NextSeq(), victim.Side, victim.Id, victim.Status));

        // 完全行动不能/行动受限/战斗不能 → 打断时光沙漏
        bool interrupts = control.Has(CombatStatus.Incapacitated)
                       || control.Has(CombatStatus.Limited)
                       || control.Has(CombatStatus.Pacified);
        if (interrupts && victim.Buffs.Get("hourglass") is { } hourglass)
            ReleaseHourglass(victim.Side, victim, hourglass, 0.8);

        return true;
    }

    private void RemoveNightBanquet(Combatant combatant)
    {
        foreach (var slot in new[] { EquipmentSlot.Z, EquipmentSlot.X })
        {
            if (combatant.Equipment.GetWorn(slot) == 26)
            {
                combatant.Equipment.Remove(slot);
                Emit(new EquipmentChangedEvent(NextSeq(), combatant.Side, slot.ToString(), null));
                EmitItemLost(combatant.Side, 26, "banquet_exhausted");
                return;
            }
        }
        if (Player(combatant.Side).Box.Contains(26))
        {
            Player(combatant.Side).Box.Remove(26);
            EmitItemLost(combatant.Side, 26, "banquet_exhausted");
        }
    }

    // ==================== 结晶之力 ====================

    private void ChooseCrystal(BattleSide side, int branch)
    {
        if (branch is < 1 or > 3)
            throw new RuleViolationException("invalid_branch");

        var combatant = Player(side).Current
            ?? throw new RuleViolationException("no_combatant");
        if (!combatant.Hero.Crystal)
            throw new RuleViolationException("no_crystal_power");
        if (combatant.CrystalActive)
            throw new RuleViolationException("crystal_already_chosen");

        combatant.CrystalActive = true;
        combatant.CrystalBranch = branch;
        ApplyCrystalEffects(combatant, branch);
        Emit(new CrystalChosenEvent(NextSeq(), side, branch));
    }

    /// <summary>结晶之力分支的永久效果（docs/01-combat-system.md §4.5）。</summary>
    private void ApplyCrystalEffects(Combatant caster, int branch)
    {
        switch (caster.Hero.Id)
        {
            case 1: // 奕阳
                if (branch == 1)
                    caster.Stats.AddMagicPenetration(0.3); // 分支1：魔穿 +30% 永久
                break;

            case 6: // 郈与却
                if (branch == 1)
                {
                    var r = caster.GetSkill(Data.SkillSlot.R);
                    if (r is not null) r.Definition = r.Definition with { Mp = r.Definition.Mp + 2 };
                }
                else if (branch == 2)
                {
                    var e = caster.GetSkill(Data.SkillSlot.E);
                    if (e is not null) e.Definition = e.Definition with { Mp = e.Definition.Mp + 2 };
                }
                break;

            case 9: // 郑心予
                if (branch == 3)
                {
                    foreach (var slot in new[] { Data.SkillSlot.Q, Data.SkillSlot.W, Data.SkillSlot.E, Data.SkillSlot.R })
                    {
                        var skill = caster.GetSkill(slot);
                        if (skill is not null)
                            skill.Definition = skill.Definition with { Mp = Math.Max(0, skill.Definition.Mp - 2) };
                    }
                }
                break;

            case 11: // 苏璟静
                if (branch == 2)
                    caster.Stats.AddAttack(4); // 分支2：基础攻击 +4 永久
                break;
        }
    }

    // ==================== 暂停 / 投降 / 掉线 ====================

    private void RequestPause(BattleSide side)
    {
        if (Phase != BattlePhase.Action)
            throw new RuleViolationException("pause_only_in_action");
        if (IsPaused)
            throw new RuleViolationException("already_paused");
        var player = Player(side);
        if (player.PausesLeft <= 0)
            throw new RuleViolationException("no_pause_left");

        player.PausesLeft--;
        player.PausedByMe = true;
        IsPaused = true;
        PauseOwner = side;
        PauseRemainingSeconds = Config.PauseSeconds;
        Emit(new PauseStateChangedEvent(NextSeq(), side, true, Config.PauseSeconds));
    }

    private void ResumePause(BattleSide side)
    {
        if (!IsPaused) return;
        // 仅发起者可主动解除（超时自动解除）
        if (PauseOwner != side) return;

        IsPaused = false;
        PauseOwner = null;
        Emit(new PauseStateChangedEvent(NextSeq(), side, false, 0));
    }

    private void Surrender(BattleSide side)
    {
        if (Round < Config.SurrenderMinRound)
            throw new RuleViolationException("surrender_too_early");
        Emit(new SurrenderEvent(NextSeq(), side));
        Finish(side.Opponent(), VictoryReason.Surrender);
    }

    /// <summary>玩家掉线：对方判胜。</summary>
    public void Disconnect(BattleSide side)
    {
        if (IsFinished) return;
        Emit(new DisconnectedEvent(NextSeq(), side));
        Finish(side.Opponent(), VictoryReason.Disconnect);
    }

    // ==================== 终局 ====================

    private void Finish(BattleSide? winner, VictoryReason reason)
    {
        if (IsFinished) return;
        Winner = winner;
        WinReason = reason;
        Phase = BattlePhase.Ended;
        _ziyuePendings.Clear();
        _windBarrierPendings.Clear();
        Emit(new BattleEndedEvent(NextSeq(), winner, reason));
    }
}
