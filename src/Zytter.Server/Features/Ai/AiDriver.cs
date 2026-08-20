using System.Collections.Concurrent;
using Zytter.Core.Battle;
using Zytter.Core.Common;
using Zytter.Core.Data;
using Zytter.Core.Drafting;
using Zytter.Core.Heroes;

namespace Zytter.Server.Features.Ai;

/// <summary>
/// 单人练习模式的 AI 驱动服务：被禁选/对局循环宿主调用，
/// 把 AiBrain 的启发式决策提交为权威命令（命令在房间门锁内执行，与玩家命令串行）。
/// 持有最小化的每房间状态（当前商店回合已购物标记），确保每个阶段只决策一次。
/// </summary>
public sealed class AiDriver
{
    private readonly GameDataCatalog _catalog = GameDataCatalog.LoadDefault();

    /// <summary>房间 → 已购物的 NextRound（每个商店回合只购物一次）。</summary>
    private readonly ConcurrentDictionary<Guid, int> _shoppedRound = new();

    // ==================== 禁选 ====================

    /// <summary>驱动 AI 完成一次禁选决策（轮到 AI 时禁用/选用，排序阶段提交出场顺序）。</summary>
    public void TickDraft(DraftSession draft, string aiSide)
    {
        if (draft.IsCompleted) return;

        if (draft.Phase == DraftPhase.Acting && draft.CurrentActingSide == aiSide)
        {
            string kind = draft.CurrentActingKind;
            var available = draft.AvailableHeroes;
            try
            {
                if (kind == "ban")
                    draft.Ban(aiSide, AiBrain.ChooseBan(_catalog, available));
                else
                    draft.Pick(aiSide, AiBrain.ChoosePick(_catalog, available));
            }
            catch (RuleViolationException)
            {
                // 非法目标兜底：弃权（写 0），避免卡住禁选流程
                if (kind == "ban") draft.Ban(aiSide, 0);
                else draft.Pick(aiSide, 0);
            }
        }
        else if (draft.Phase == DraftPhase.Ordering)
        {
            int[]? order = aiSide == "A" ? draft.OrderA : draft.OrderB;
            if (order is null)
            {
                var picks = aiSide == "A" ? draft.PicksA : draft.PicksB;
                if (picks.Count > 0)
                    draft.SubmitOrder(aiSide, picks.ToArray());
            }
        }
    }

    // ==================== 对局 ====================

    /// <summary>驱动 AI 完成本 tick 的对局决策（复苏/装备/行动/购物/结晶）。</summary>
    public void TickBattle(BattleSession session, BattleSide aiSide, Guid roomId)
    {
        if (session.IsFinished) return;

        switch (session.Phase)
        {
            case BattlePhase.Prepare:
                HandleRevive(session, aiSide);
                break;

            case BattlePhase.Action:
                HandleEquip(session, aiSide);
                HandleAction(session, aiSide);
                break;

            case BattlePhase.Shop:
                HandleShop(session, aiSide, roomId);
                break;
        }

        HandleCrystal(session, aiSide);
    }

    /// <summary>行动受限时使用复苏胶囊解除控制。</summary>
    private static void HandleRevive(BattleSession session, BattleSide aiSide)
    {
        var me = session.Player(aiSide).Current;
        if (me is null || !me.Status.Has(CombatStatus.Limited)) return;

        try
        {
            session.Execute(new ReviveChoiceCommand(aiSide, AiBrain.ChooseRevive(session, aiSide)));
        }
        catch (RuleViolationException) { /* 无复苏道具时忽略 */ }
    }

    /// <summary>行动阶段尝试穿戴盒中装备（穿戴后装备离开道具盒，天然幂等）。</summary>
    private static void HandleEquip(BattleSession session, BattleSide aiSide)
    {
        int? itemId = AiBrain.ChooseEquip(session, aiSide);
        if (itemId is null) return;

        var me = session.Player(aiSide).Current;
        if (me is null) return;
        var slot = me.Equipment.GetWorn(EquipmentSlot.Z) is null ? EquipmentSlot.Z : EquipmentSlot.X;

        try
        {
            session.Execute(new EquipCommand(aiSide, slot, itemId.Value));
        }
        catch (RuleViolationException) { /* 槽位/物品变化时忽略 */ }
    }

    /// <summary>行动阶段提交行动：按候选优先级依次尝试，校验失败自动回退。</summary>
    private static void HandleAction(BattleSession session, BattleSide aiSide)
    {
        if (session.HasPendingAction(aiSide)) return;

        foreach (var action in AiBrain.BuildActionCandidates(session, aiSide))
        {
            try
            {
                session.Execute(new SubmitActionCommand(aiSide, action));
                return;
            }
            catch (RuleViolationException)
            {
                // 该候选不合法（魔法不足/被控/目标不符等），尝试下一候选
            }
        }
    }

    /// <summary>商店回合购物（每个商店回合仅决策一次）。</summary>
    private void HandleShop(BattleSession session, BattleSide aiSide, Guid roomId)
    {
        if (_shoppedRound.TryGetValue(roomId, out int done) && done == session.NextRound)
            return;

        int? itemId = AiBrain.ChooseShopPurchase(session, aiSide);
        if (itemId is not null)
        {
            try
            {
                session.Execute(new ShopPurchaseCommand(aiSide, itemId.Value));
            }
            catch (RuleViolationException) { /* 金币/限购变化时忽略 */ }
        }

        _shoppedRound[roomId] = session.NextRound;
    }

    /// <summary>结晶之力就绪时选择分支（默认分支 1）。</summary>
    private static void HandleCrystal(BattleSession session, BattleSide aiSide)
    {
        var me = session.Player(aiSide).Current;
        if (me is null || me.IsDead) return;
        if (!me.Hero.Crystal || me.CrystalActive) return;
        if (me.HeroTime < 7 && me.DamageDealt < 20) return;

        try
        {
            session.Execute(new CrystalChoiceCommand(aiSide, AiBrain.ChooseCrystalBranch()));
        }
        catch (RuleViolationException) { /* 已选择时忽略 */ }
    }

    /// <summary>房间清理时释放其状态。</summary>
    public void Forget(Guid roomId) => _shoppedRound.TryRemove(roomId, out _);
}
