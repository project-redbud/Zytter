using Zytter.Core.Common;

namespace Zytter.Core.Buffs;

/// <summary>Buff 施加时对已存在实例的持续回合处理方式。</summary>
public enum BuffApplyMode
{
    /// <summary>刷新为新的持续回合（原版"叠加刷新"）。</summary>
    Refresh,

    /// <summary>保留原剩余回合（原版"不可叠加"）。</summary>
    Keep,

    /// <summary>剩余回合叠加（原版"影响的回合数可叠加"，如 yyER+=4）。</summary>
    Extend,
}

/// <summary>
/// 战斗单位的 Buff 容器：按 BuffId 聚合实例，负责施加/叠加/查询/回合递减/移除。
/// 迭代顺序稳定（InsertionOrder），保证确定性重放。
/// </summary>
public sealed class BuffContainer
{
    private readonly Dictionary<string, BuffInstance> _buffs = new(StringComparer.Ordinal);

    public IReadOnlyCollection<BuffInstance> All => _buffs.Values;

    public bool Has(string buffId) => _buffs.ContainsKey(buffId);

    public BuffInstance? Get(string buffId) =>
        _buffs.TryGetValue(buffId, out var buff) ? buff : null;

    public int GetStacks(string buffId) => Get(buffId)?.Stacks ?? 0;

    /// <summary>
    /// 施加/叠加 Buff。
    /// 若已存在：stacks 增量叠加，持续回合按 <paramref name="mode"/> 处理
    /// （Refresh 替换 / Keep 保留 / Extend 叠加）。
    /// 说明：回合制 Buff 在回合内施加，统一在回合开始时递减，
    /// 因此"持续 N 回合"语义 = 施加后 N 个完整回合行动阶段生效，存储 N+1 回合计数。
    /// </summary>
    public BuffInstance Apply(Data.BuffDefinition definition, int stacks, int durationRounds, BuffApplyMode mode = BuffApplyMode.Refresh)
    {
        if (_buffs.TryGetValue(definition.Id, out var existing))
        {
            existing.Stacks += stacks;
            if (durationRounds >= 0)
            {
                existing.RemainingRounds = mode switch
                {
                    BuffApplyMode.Refresh => durationRounds,
                    BuffApplyMode.Extend => existing.RemainingRounds < 0 ? durationRounds : existing.RemainingRounds + durationRounds,
                    BuffApplyMode.Keep => existing.RemainingRounds,
                    _ => existing.RemainingRounds,
                };
            }
            return existing;
        }

        var created = new BuffInstance(definition, stacks, durationRounds);
        _buffs[definition.Id] = created;
        return created;
    }

    public bool Remove(string buffId) => _buffs.Remove(buffId);

    /// <summary>
    /// 回合开始递减：所有有时限的 Buff 回合数 -1，返回到期移除的 Buff 列表（供触发 Removed 挂点）。
    /// </summary>
    public List<BuffInstance> TickTurnStart()
    {
        var expired = new List<BuffInstance>();
        foreach (var buff in _buffs.Values.ToList())
        {
            if (buff.IsPermanent) continue;
            buff.RemainingRounds--;
            if (buff.IsExpired)
            {
                _buffs.Remove(buff.Definition.Id);
                expired.Add(buff);
            }
        }
        return expired;
    }

    /// <summary>清空全部 Buff（英雄死亡换人时）。</summary>
    public void Clear() => _buffs.Clear();
}
