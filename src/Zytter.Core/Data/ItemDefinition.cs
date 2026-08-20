namespace Zytter.Core.Data;

/// <summary>物品类别。id 1~12 为消耗品，13~27 为装备（原版分类）。</summary>
public enum ItemKind
{
    Consumable,
    Equipment,
}

/// <summary>装备穿戴属性加成（数据驱动，零值表示无此加成）。</summary>
public sealed class EquipmentStats
{
    public int Atk { get; init; }
    public int Def { get; init; }
    public int Adf { get; init; }
    public int Xdl { get; init; }
    public int Mpp { get; init; }
    public int Hpp { get; init; }
    public double Adp { get; init; }
    public double App { get; init; }
}

/// <summary>
/// 物品静态定义（数据驱动，来源于原版 MySQL items 表，含真实价格）。
/// </summary>
public sealed class ItemDefinition
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Describe { get; init; } = "";
    public int Gold { get; init; }
    public ItemKind Kind { get; init; }

    /// <summary>效果管线注册键（如 revive / ziyue_staff）。</summary>
    public string Effect { get; init; } = "";

    public EquipmentStats Stats { get; init; } = new();

    public override string ToString() => $"{Name}(#{Id})";
}
