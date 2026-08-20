using Zytter.Core.Common;

namespace Zytter.Core.Economy;

/// <summary>
/// 道具盒：容量上限 30（原版规则），存放消耗品与脱下/未穿戴的装备。
/// </summary>
public sealed class ItemBox
{
    private readonly List<int> _items = new();

    public int Capacity { get; init; } = 30;

    public IReadOnlyList<int> Items => _items;

    public int Count => _items.Count;

    public int CountOf(int itemId) => _items.Count(id => id == itemId);

    public bool Contains(int itemId) => _items.Contains(itemId);

    /// <summary>加入道具盒；满则抛规则违规（reason=item_box_full）。</summary>
    public void Add(int itemId)
    {
        if (_items.Count >= Capacity)
            throw new RuleViolationException("item_box_full", $"道具盒已满（{Capacity} 件）");
        _items.Add(itemId);
    }

    /// <summary>取出一件物品；不存在则抛规则违规（reason=item_not_found）。</summary>
    public void Remove(int itemId)
    {
        if (!_items.Remove(itemId))
            throw new RuleViolationException("item_not_found", $"道具盒中没有该物品 #{itemId}");
    }
}
