namespace Zytter.Core.Battle;

/// <summary>装备槽位（原版 Z/X 两个穿戴槽）。</summary>
public enum EquipmentSlot
{
    Z,
    X,
}

/// <summary>
/// 英雄装备状态：两个穿戴槽 + "吃装备"永久获得列表。
/// 英雄死亡时穿戴中的装备脱下回道具盒（由 BattleSession 处理）。
/// </summary>
public sealed class Equipment
{
    private readonly Data.GameDataCatalog _catalog;
    private readonly Dictionary<EquipmentSlot, int> _worn = new();

    public Equipment(Data.GameDataCatalog catalog)
    {
        _catalog = catalog;
    }

    /// <summary>已"吃"的装备（加时赛重复购买永久获得，不占槽位，不因死亡脱下）。</summary>
    public List<int> Consumed { get; } = new();

    /// <summary>当前穿戴的物品 ID（含永久装备）。</summary>
    public IEnumerable<int> WornIds => _worn.Values.Concat(Consumed);

    public int? GetWorn(EquipmentSlot slot) =>
        _worn.TryGetValue(slot, out var itemId) ? itemId : null;

    /// <summary>穿戴装备，返回被替换下来的旧装备 ID（空槽返回 null，不能返回默认值 0）。</summary>
    public int? Wear(EquipmentSlot slot, int itemId)
    {
        int? old = _worn.TryGetValue(slot, out var worn) ? worn : null;
        _worn[slot] = itemId;
        return old;
    }

    /// <summary>脱下装备，返回被脱下的装备 ID（可能为 null）。</summary>
    public int? Remove(EquipmentSlot slot)
    {
        if (!_worn.Remove(slot, out var old))
            return null;
        return old;
    }

    public bool IsWorn(int itemId) => _worn.ContainsValue(itemId) || Consumed.Contains(itemId);

    /// <summary>装备提供的属性加成合计（由装备定义数据求和）。</summary>
    public Data.EquipmentStats StatBonuses
    {
        get
        {
            int atk = 0, def = 0, adf = 0, xdl = 0, mpp = 0, hpp = 0;
            double adp = 0, app = 0;
            foreach (var itemId in WornIds)
            {
                var stats = _catalog.GetItem(itemId).Stats;
                atk += stats.Atk;
                def += stats.Def;
                adf += stats.Adf;
                xdl += stats.Xdl;
                mpp += stats.Mpp;
                hpp += stats.Hpp;
                adp += stats.Adp;
                app += stats.App;
            }
            return new Data.EquipmentStats
            {
                Atk = atk,
                Def = def,
                Adf = adf,
                Xdl = xdl,
                Mpp = mpp,
                Hpp = hpp,
                Adp = adp,
                App = app,
            };
        }
    }
}
