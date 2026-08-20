namespace Zytter.Core.Heroes;

/// <summary>
/// 战斗中英雄的属性快照。
/// 旧版中属性与 UI 数据混在一个实体里（Hero 同时是数据库行、Swing 面板数据、战斗状态）；
/// 新版静态定义在 HeroDefinition，战斗中的可变数值全在此类，互不污染。
/// </summary>
public sealed class HeroStats
{
    public int MaxHp { get; private set; }
    public int Hp { get; private set; }
    public int MaxMp { get; private set; }
    public int Mp { get; private set; }

    /// <summary>基础攻击力（生效值 = 基础值 + Buff 修正，经 StatsResolver 计算）。</summary>
    public int Attack { get; private set; }

    /// <summary>基础物理护甲（旧 def）。</summary>
    public int Defense { get; private set; }

    /// <summary>基础魔法防御（旧 adf）。</summary>
    public int MagicDefense { get; private set; }

    /// <summary>基础行动力（旧 xdl），参与行动顺序算法。</summary>
    public int ActionPower { get; private set; }

    /// <summary>每回合生命回复（旧 hpp）。</summary>
    public int HpRegen { get; private set; }

    /// <summary>每回合魔法回复（旧 mpp）。</summary>
    public int MpRegen { get; private set; }

    /// <summary>物理穿透（旧 adp，百分比）。</summary>
    public double ArmorPenetration { get; private set; }

    /// <summary>魔法穿透（旧 app，百分比）。</summary>
    public double MagicPenetration { get; private set; }

    /// <summary>物理伤害减免（旧 defrate，百分比）。</summary>
    public double PhysicalDamageReduction { get; private set; }

    public bool IsDead => Hp <= 0;

    public HeroStats(
        int maxHp, int maxMp, int attack, int defense, int magicDefense,
        int actionPower, int hpRegen, int mpRegen,
        double armorPenetration, double magicPenetration, double physicalDamageReduction)
    {
        MaxHp = maxHp;
        Hp = maxHp;
        MaxMp = maxMp;
        Mp = maxMp;
        Attack = attack;
        Defense = defense;
        MagicDefense = magicDefense;
        ActionPower = actionPower;
        HpRegen = hpRegen;
        MpRegen = mpRegen;
        ArmorPenetration = armorPenetration;
        MagicPenetration = magicPenetration;
        PhysicalDamageReduction = physicalDamageReduction;
    }

    /// <summary>调整生命值并钳制在 [0, MaxHp]。返回实际变化量。</summary>
    public int AddHp(int delta)
    {
        int before = Hp;
        Hp = Math.Clamp(Hp + delta, 0, MaxHp);
        return Hp - before;
    }

    /// <summary>调整魔法值并钳制在 [0, MaxMp]。返回实际变化量。</summary>
    public int AddMp(int delta)
    {
        int before = Mp;
        Mp = Math.Clamp(Mp + delta, 0, MaxMp);
        return Mp - before;
    }

    // ---- 永久属性调整（界限突破、圣歌等"永久"效果直接修改基础值；
    //      临时效果一律走 Buff 挂点而非直接改属性，避免旧版"加回来忘了扣"的 bug）----

    public void AddMaxHp(int delta)
    {
        MaxHp += delta;
        Hp = Math.Clamp(Hp, 0, MaxHp);
    }

    public void AddMaxMp(int delta)
    {
        MaxMp += delta;
        Mp = Math.Clamp(Mp, 0, MaxMp);
    }

    public void AddAttack(int delta) => Attack += delta;

    /// <summary>护甲可被降到负数（强力剥削等，原版允许负护甲放大伤害）。</summary>
    public void AddDefense(int delta) => Defense += delta;

    public void AddMagicDefense(int delta) => MagicDefense += delta;

    public void AddActionPower(int delta) => ActionPower += delta;

    public void AddHpRegen(int delta) => HpRegen += delta;

    public void AddMpRegen(int delta) => MpRegen += delta;

    public void AddArmorPenetration(double delta) => ArmorPenetration += delta;

    public void AddMagicPenetration(double delta) => MagicPenetration += delta;

    public void AddPhysicalDamageReduction(double delta) => PhysicalDamageReduction += delta;
}
