using Zytter.Core.Battle;
using Zytter.Core.Common;
using Zytter.Core.Data;

namespace Zytter.Core.Items;

/// <summary>消耗品效果实现（id 3~12 战斗内使用；id 1/2 商店即时，由 ShopPurchase 处理）。</summary>
public static class ItemEffectsInstaller
{
    public static void Install(BattleSession session)
    {
        var reg = session.ItemEffects;

        reg.Register("potion_mid", new SimpleItemEffect((s, ctx) =>
        {
            ctx.User.Stats.AddHp(6);
            int mp = (int)Math.Round(ctx.User.Stats.MaxMp * 0.3);
            ctx.User.Stats.AddMp(mp);
            s.Emit(new HealedEvent(s.NextSeq(), ctx.User.Side, ctx.User.Id, 6));
        }));

        reg.Register("potion_large", new SimpleItemEffect((s, ctx) =>
        {
            ctx.User.Stats.AddHp(11);
            int mp = (int)Math.Round(ctx.User.Stats.MaxMp * 0.6);
            ctx.User.Stats.AddMp(mp);
            s.Emit(new HealedEvent(s.NextSeq(), ctx.User.Side, ctx.User.Id, 11));
        }));

        reg.Register("revive", new SimpleItemEffect((s, ctx) =>
        {
            s.ClearControlStatus(ctx.User);
            s.ApplyRevival(ctx.User);
        }));

        reg.Register("revive_plus", new SimpleItemEffect((s, ctx) =>
        {
            ctx.User.Stats.AddHp(5);
            s.Emit(new HealedEvent(s.NextSeq(), ctx.User.Side, ctx.User.Id, 5));
            s.ClearControlStatus(ctx.User);
            s.ApplyRevival(ctx.User);
        }));

        reg.Register("mp_filler_1", new SimpleItemEffect((s, ctx) => ctx.User.Stats.AddMp(7)));
        reg.Register("mp_filler_2", new SimpleItemEffect((s, ctx) => ctx.User.Stats.AddMp(ctx.User.Stats.MaxMp)));

        reg.Register("mp_filler_3", new SimpleItemEffect((s, ctx) =>
        {
            ctx.User.Stats.AddMp(ctx.User.Stats.MaxMp);
            s.ApplyBuff(ctx.User, s.Catalog.GetBuff("mp_filler_iii"), 1, 3, Buffs.BuffApplyMode.Refresh);
        }));

        reg.Register("ap_capsule", new SimpleItemEffect((s, ctx) =>
            s.ApplyBuff(ctx.User, s.Catalog.GetBuff("ap_capsule"), 1, 3, Buffs.BuffApplyMode.Refresh)));

        reg.Register("resist_patch", new SimpleItemEffect((s, ctx) =>
        {
            s.ApplyBuff(ctx.User, s.Catalog.GetBuff("resist_patch_def"), 1, 3, Buffs.BuffApplyMode.Refresh);
            s.ApplyBuff(ctx.User, s.Catalog.GetBuff("resist_patch_mdf"), 1, 3, Buffs.BuffApplyMode.Refresh);
        }));

        reg.Register("power_potion", new SimpleItemEffect((s, ctx) =>
            s.ApplyBuff(ctx.User, s.Catalog.GetBuff("power_potion"), 1, 3, Buffs.BuffApplyMode.Refresh)));
    }
}

/// <summary>无被动触发点的简单消耗品。</summary>
public sealed class SimpleItemEffect : IItemEffect
{
    private readonly Action<BattleSession, ItemContext> _use;

    public SimpleItemEffect(Action<BattleSession, ItemContext> use)
    {
        _use = use;
    }

    public void Use(ItemContext ctx) => _use(ctx.Session, ctx);

    public void OnPassive(ItemPassiveHook hook, ItemContext ctx)
    {
    }
}
