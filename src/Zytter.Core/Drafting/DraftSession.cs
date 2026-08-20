using Zytter.Core.Common;

namespace Zytter.Core.Drafting;

/// <summary>禁选阶段。</summary>
public enum DraftPhase
{
    /// <summary>准备（5 秒，展示候选池）。</summary>
    Ready,

    /// <summary>禁用/选用步骤进行中。</summary>
    Acting,

    /// <summary>阶段过渡（BAN→PICK 间 2 秒）。</summary>
    Transition,

    /// <summary>排序决策（22 秒，排出场顺序）。</summary>
    Ordering,

    /// <summary>完成/作废。</summary>
    Completed,
}

/// <summary>
/// 禁选（B/P）权威状态机（docs/03 §4.4 规则）：
/// 顺序 P1B1→P2B1→P1B2→P2B2 → 2s 过渡 → P1P1→P2P1→P1P2→P2P2→P1P3→P2P3，
/// P1（房主，A 方）全程先手；禁/选即出池；超时弃权（写 0）；排序超时按选用顺序自动提交。
/// 该流程在服务器权威执行，随机数与状态不依赖客户端。
/// </summary>
public sealed class DraftSession
{
    public const int ReadySeconds = 5;
    public const int BanSeconds = 10;
    public const int PickSeconds = 20;
    public const int TransitionSeconds = 2;
    public const int OrderSeconds = 22;
    public const int MaxHeroes = 3;

    public Guid RoomId { get; }
    public IReadOnlyList<int> HeroPool { get; }

    public DraftPhase Phase { get; private set; } = DraftPhase.Ready;
    public double StepRemainingSeconds { get; private set; } = ReadySeconds;

    /// <summary>当前步骤下标（0~9：4 BAN + 6 PICK），仅 Acting 阶段有效。</summary>
    public int StepIndex { get; private set; }

    public List<int> BansA { get; } = new();
    public List<int> BansB { get; } = new();
    public List<int> PicksA { get; } = new();
    public List<int> PicksB { get; } = new();

    /// <summary>已提交的出场顺序（null 表示未提交）。</summary>
    public int[]? OrderA { get; private set; }
    public int[]? OrderB { get; private set; }

    public bool IsCompleted => Phase == DraftPhase.Completed;

    /// <summary>完成时双方最终阵容（null 表示流程作废，如某方 0 名英雄）。</summary>
    public (int[] RosterA, int[] RosterB)? Result { get; private set; }

    public event Action<DraftEvent>? EventEmitted;

    private readonly List<DraftEvent> _pendingEvents = new();

    /// <summary>取出并清空待广播事件。</summary>
    public IReadOnlyList<DraftEvent> DrainEvents()
    {
        if (_pendingEvents.Count == 0) return Array.Empty<DraftEvent>();
        var snapshot = _pendingEvents.ToArray();
        _pendingEvents.Clear();
        return snapshot;
    }

    /// <summary>步骤表：4 次禁用（A B A B）+ 6 次选用（A B A B A B）。</summary>
    private static readonly (string Kind, string Side)[] Steps =
    {
        ("ban", "A"), ("ban", "B"), ("ban", "A"), ("ban", "B"),
        ("pick", "A"), ("pick", "B"), ("pick", "A"), ("pick", "B"), ("pick", "A"), ("pick", "B"),
    };

    public DraftSession(Guid roomId, IReadOnlyList<int> heroPool)
    {
        if (heroPool.Count == 0)
            throw new GameDataException("禁选候选池不能为空");
        RoomId = roomId;
        HeroPool = heroPool;
    }

    public void Start()
    {
        Emit(new DraftStartedEvent(HeroPool.ToArray()));
    }

    /// <summary>推进时钟（服务器按节拍调用）。</summary>
    public void Tick(double deltaSeconds)
    {
        if (IsCompleted) return;
        StepRemainingSeconds -= deltaSeconds;
        if (StepRemainingSeconds > 0) return;

        switch (Phase)
        {
            case DraftPhase.Ready:
                BeginStep(0);
                break;
            case DraftPhase.Acting:
                // 超时弃权（BAN/PICK 写 0）
                var (kind, side) = Steps[StepIndex];
                if (kind == "ban")
                    Ban(side, 0);
                else
                    Pick(side, 0);
                break;
            case DraftPhase.Transition:
                BeginStep(4); // 进入 PICK 阶段
                break;
            case DraftPhase.Ordering:
                // 排序超时：按选用顺序自动提交
                if (OrderA is null) SubmitOrder("A", PicksA.ToArray());
                if (OrderB is null) SubmitOrder("B", PicksB.ToArray());
                Complete();
                break;
        }
    }

    private void BeginStep(int stepIndex)
    {
        StepIndex = stepIndex;
        var (kind, side) = Steps[stepIndex];
        Phase = DraftPhase.Acting;
        StepRemainingSeconds = kind == "ban" ? BanSeconds : PickSeconds;
        Emit(new DraftStepChangedEvent(stepIndex, kind, side, (int)StepRemainingSeconds));
    }

    private bool IsSideTurn(string side) =>
        Phase == DraftPhase.Acting && Steps[StepIndex].Side == side;

    public void Ban(string side, int heroId)
    {
        if (!IsSideTurn(side) || Steps[StepIndex].Kind != "ban")
            throw new RuleViolationException("not_your_ban_turn");

        var bans = side == "A" ? BansA : BansB;
        if (heroId != 0 && (bans.Contains(heroId) || !HeroPool.Contains(heroId)))
            throw new RuleViolationException("invalid_ban_target");
        bans.Add(heroId);
        Emit(new HeroBannedEvent(side, heroId));

        if (StepIndex == 3)
        {
            Phase = DraftPhase.Transition;
            StepRemainingSeconds = TransitionSeconds;
        }
        else
        {
            BeginStep(StepIndex + 1);
        }
    }

    public void Pick(string side, int heroId)
    {
        if (!IsSideTurn(side) || Steps[StepIndex].Kind != "pick")
            throw new RuleViolationException("not_your_pick_turn");

        var picks = side == "A" ? PicksA : PicksB;
        if (picks.Count >= MaxHeroes)
            throw new RuleViolationException("pick_full");

        if (heroId == 0)
        {
            // 弃权：不计入阵容（原版写 0 = 放弃该次选用机会）
            Emit(new HeroPickedEvent(side, 0));
            AdvanceAfterPick();
            return;
        }

        if (picks.Contains(heroId) || !HeroPool.Contains(heroId) || IsRemoved(heroId))
            throw new RuleViolationException("invalid_pick_target");
        picks.Add(heroId);
        Emit(new HeroPickedEvent(side, heroId));
        AdvanceAfterPick();
    }

    private void AdvanceAfterPick()
    {
        if (StepIndex == Steps.Length - 1)
        {
            Phase = DraftPhase.Ordering;
            StepRemainingSeconds = OrderSeconds;
            Emit(new DraftOrderPhaseEvent(OrderSeconds));
        }
        else
        {
            BeginStep(StepIndex + 1);
        }
    }

    /// <summary>提交出场顺序（必须是本方 PICK 的排列）。</summary>
    public void SubmitOrder(string side, IReadOnlyList<int> order)
    {
        if (Phase != DraftPhase.Ordering)
            throw new RuleViolationException("not_ordering_phase");
        var picks = side == "A" ? PicksA : PicksB;
        if (order.Count != picks.Count || order.Any(id => !picks.Contains(id)) || order.Distinct().Count() != order.Count)
            throw new RuleViolationException("invalid_order");

        if (side == "A") OrderA = order.ToArray();
        else OrderB = order.ToArray();
        Emit(new DraftOrderedEvent(side, order.ToArray()));

        if (OrderA is not null && OrderB is not null)
            Complete();
    }

    private void Complete()
    {
        if (IsCompleted) return; // 幂等：SubmitOrder 内部可能已触发完成

        Phase = DraftPhase.Completed;
        StepRemainingSeconds = 0;

        // 某方 0 名英雄 → 对局作废（原版：双方无英雄则对局作废）
        if (PicksA.Count == 0 || PicksB.Count == 0)
        {
            Result = null;
            Emit(new DraftCompletedEvent(Array.Empty<int>(), Array.Empty<int>()));
            return;
        }

        Result = (OrderA ?? PicksA.ToArray(), OrderB ?? PicksB.ToArray());
        Emit(new DraftCompletedEvent(Result.Value.RosterA, Result.Value.RosterB));
    }

    /// <summary>英雄是否已出池（被禁或已被选）。</summary>
    public bool IsRemoved(int heroId) =>
        BansA.Contains(heroId) || BansB.Contains(heroId) || PicksA.Contains(heroId) || PicksB.Contains(heroId);

    /// <summary>当前待行动方（仅 Acting 阶段；否则返回空串）。供服务器侧 AI 驱动使用。</summary>
    public string CurrentActingSide => Phase == DraftPhase.Acting ? Steps[StepIndex].Side : "";

    /// <summary>当前步骤类型（"ban"/"pick"；仅 Acting 阶段）。供服务器侧 AI 驱动使用。</summary>
    public string CurrentActingKind => Phase == DraftPhase.Acting ? Steps[StepIndex].Kind : "";

    /// <summary>当前可选的英雄（候选区剩余）。</summary>
    public IReadOnlyList<int> AvailableHeroes => HeroPool.Where(id => !IsRemoved(id)).ToList();

    private void Emit(DraftEvent e)
    {
        _pendingEvents.Add(e);
        EventEmitted?.Invoke(e);
    }
}
