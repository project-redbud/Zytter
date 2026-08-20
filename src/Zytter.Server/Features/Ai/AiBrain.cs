using Zytter.Core.Battle;
using Zytter.Core.Data;
using Zytter.Core.Heroes;

namespace Zytter.Server.Features.Ai;

/// <summary>
/// 单人练习模式的服务器侧 AI 决策（纯函数式启发策略，无状态）。
/// 决策只读会话并返回「意图」，实际执行由 AiDriver 提交为权威命令，
/// 引擎校验失败时按候选优先级回退（保证永不因非法操作卡死）。
/// </summary>
public static class AiBrain
{
    // ==================== 禁选（B/P） ====================

    /// <summary>选择要禁用的英雄：禁用综合战力最高的候选（弱化对手）。</summary>
    public static int ChooseBan(GameDataCatalog catalog, IReadOnlyList<int> available)
    {
        if (available.Count == 0) return 0;
        return available.OrderByDescending(id => HeroPower(catalog.GetHero(id))).First();
    }

    /// <summary>选择要选用的英雄：选取综合战力最高的候选。</summary>
    public static int ChoosePick(GameDataCatalog catalog, IReadOnlyList<int> available)
    {
        if (available.Count == 0) return 0;
        return available.OrderByDescending(id => HeroPower(catalog.GetHero(id))).First();
    }

    /// <summary>英雄综合战力打分（生命/攻击权重最高，兼顾护甲魔抗行动力回蓝）。</summary>
    private static int HeroPower(HeroDefinition h) =>
        h.Hp * 2 + h.Atk * 3 + h.Def * 2 + h.Adf * 2 + h.Move * 2 + h.Remp;

    // ==================== 战斗行动 ====================

    /// <summary>
    /// 构建行动候选（按优先级从高到低）：低血喝药 → 敌方目标技能(R→E→W→Q) → 普攻 → 放弃。
    /// AI 驱动依次尝试，引擎校验失败（如状态/魔法不足）时自动回退到下一候选。
    /// </summary>
    public static IReadOnlyList<PlayerAction> BuildActionCandidates(BattleSession session, BattleSide side)
    {
        var me = session.Player(side).Current;
        if (me is null || me.IsDead)
            return new PlayerAction[] { new SkipAction() };

        var candidates = new List<PlayerAction>();
        var player = session.Player(side);

        // 1. 低血量时喝药（大回复药优先于中回复药）
        if (me.Stats.Hp < me.Stats.MaxHp * 0.5)
        {
            if (player.Box.Contains(4)) candidates.Add(new UseItemAction(4));
            else if (player.Box.Contains(3)) candidates.Add(new UseItemAction(3));
        }

        // 2. 可施法时释放敌方目标技能（终极技优先）
        if (me.Status.CanCast())
        {
            foreach (var slot in new[] { SkillSlot.R, SkillSlot.E, SkillSlot.W, SkillSlot.Q })
            {
                var skill = me.GetSkill(slot);
                if (skill is null) continue;
                if (skill.Definition.Target != SkillTarget.Enemy) continue;
                if (me.Stats.Mp >= skill.Definition.Mp)
                    candidates.Add(new CastSkillAction(slot));
            }
        }

        // 3. 可普攻时普攻
        if (me.Status.CanBasicAttack())
            candidates.Add(new BasicAttackAction());

        // 4. 兜底：放弃
        candidates.Add(new SkipAction());
        return candidates;
    }

    // ==================== 商店 ====================

    /// <summary>挑选本回合要购买的物品（null 表示不买）。</summary>
    public static int? ChooseShopPurchase(BattleSession session, BattleSide side)
    {
        var player = session.Player(side);
        var me = player.Current;
        var catalog = session.Catalog;
        int gold = player.Wallet.Gold;
        bool overtime = session.NextRound >= session.Config.InitialRoundLimit;

        // 回复类道具：血量偏低时补充（加时赛商店不再出售回复类）
        if (!overtime && me is not null && !me.IsDead && me.Stats.Hp < me.Stats.MaxHp * 0.6)
        {
            foreach (var potion in new[] { 4, 3 })
            {
                if (gold >= catalog.GetItem(potion).Gold && player.Box.CountOf(potion) < 3)
                    return potion;
            }
        }

        // 装备：购买尚未拥有且买得起的最高性价比装备
        int? best = null;
        double bestScore = double.MinValue;
        foreach (var item in catalog.Items.Values.Where(i => i.Kind == ItemKind.Equipment))
        {
            if (item.Gold > gold) continue;
            if (me?.Equipment.IsWorn(item.Id) == true || player.Box.Contains(item.Id)) continue;
            double score = EquipmentScore(item);
            if (score > bestScore)
            {
                bestScore = score;
                best = item.Id;
            }
        }
        return best;
    }

    /// <summary>装备性价比打分（简单加权；无属性被动给固定分）。</summary>
    private static double EquipmentScore(ItemDefinition item)
    {
        var s = item.Stats;
        double score = s.Atk * 3 + s.Def * 2 + s.Adf * 2 + s.Xdl * 3 + s.Mpp * 2 + s.Hpp * 2
                       + s.Adp * 12 + s.App * 12;
        score += item.Effect switch
        {
            "eagle_bow" => 5,      // 普攻伤害 +42%
            "tough_shield" => 6,   // 物理伤害减免 25%
            "night_banquet" => 3,  // 抵挡 3 次强控制
            _ => 0,
        };
        return score;
    }

    /// <summary>挑选要穿戴的装备（盒中第一件尚未穿戴的装备，null 表示无）。</summary>
    public static int? ChooseEquip(BattleSession session, BattleSide side)
    {
        var player = session.Player(side);
        var me = player.Current;
        if (me is null) return null;

        bool hasFreeSlot = me.Equipment.GetWorn(EquipmentSlot.Z) is null
                        || me.Equipment.GetWorn(EquipmentSlot.X) is null;
        if (!hasFreeSlot) return null;

        foreach (var itemId in player.Box.Items)
        {
            if (session.Catalog.GetItem(itemId).Kind == ItemKind.Equipment)
                return itemId;
        }
        return null;
    }

    /// <summary>结晶之力分支选择（固定分支 1，简单策略）。</summary>
    public static int ChooseCrystalBranch() => 1;

    /// <summary>复苏选择：有高级复苏胶囊用高级，否则普通，再否则取消。</summary>
    public static ReviveChoice ChooseRevive(BattleSession session, BattleSide side)
    {
        var player = session.Player(side);
        if (player.Box.Contains(6)) return ReviveChoice.UseRevivePlus;
        if (player.Box.Contains(5)) return ReviveChoice.UseRevive;
        return ReviveChoice.Cancel;
    }
}
