using Zytter.Core.Data;

namespace Zytter.Core.Tests;

public class GameDataCatalogTests
{
    private static readonly GameDataCatalog Catalog = GameDataCatalog.LoadDefault();

    [Fact]
    public void LoadsAllHeroes()
    {
        Assert.Equal(12, Catalog.Heroes.Count);
        Assert.Equal("奕阳", Catalog.GetHero(1).Name);
        Assert.Equal("维多利娜", Catalog.GetHero(12).Name);
    }

    [Fact]
    public void LoadsAllSkills()
    {
        Assert.Equal(36, Catalog.Skills.Count);
        Assert.Equal("烈日之箭", Catalog.GetSkill(1).Name);
        Assert.Equal(5, Catalog.GetSkill(1).Mp);
        Assert.Equal("圣歌", Catalog.GetSkill(36).Name);
    }

    [Fact]
    public void LoadsAllItems()
    {
        Assert.Equal(27, Catalog.Items.Count);
        Assert.Equal(3, Catalog.GetItem(1).Gold);
        Assert.Equal(ItemKind.Equipment, Catalog.GetItem(13).Kind);
    }

    [Fact]
    public void HeroSkillLinksAreValid()
    {
        foreach (var hero in Catalog.Heroes.Values)
        {
            foreach (var skillId in hero.SkillIds)
            {
                Assert.True(Catalog.Skills.ContainsKey(skillId), $"{hero.Name} 引用了缺失技能 #{skillId}");
            }
        }
    }

    [Fact]
    public void CrystalHeroesAreCorrect()
    {
        Assert.True(Catalog.GetHero(1).Crystal);   // 奕阳
        Assert.True(Catalog.GetHero(6).Crystal);   // 郈与却
        Assert.True(Catalog.GetHero(9).Crystal);   // 郑心予
        Assert.True(Catalog.GetHero(11).Crystal);  // 苏璟静
        Assert.False(Catalog.GetHero(2).Crystal);
    }

    [Fact]
    public void HeroStatsSnapshotMatchesDatabase()
    {
        // 原版 MySQL heroes 表数值
        var yy = Catalog.GetHero(1).CreateStats();
        Assert.Equal(31, yy.MaxHp);
        Assert.Equal(18, yy.MaxMp);
        Assert.Equal(6, yy.Attack);
        Assert.Equal(4, yy.ActionPower);
        Assert.Equal(3, yy.MpRegen);
    }

    [Fact]
    public void EquipmentStatsAreParsed()
    {
        var staff = Catalog.GetItem(13); // 紫月神杖
        Assert.Equal(2, staff.Stats.Xdl);
        Assert.Equal(2, staff.Stats.Mpp);

        var sword = Catalog.GetItem(15); // 长剑-朝醉青烟
        Assert.Equal(5, sword.Stats.Atk);
        Assert.Equal(0.3, sword.Stats.Adp);
    }
}
