using Godot;
using Zytter.Core.Data;

namespace Zytter.Client;

/// <summary>
/// 英雄 &amp; 物品图鉴（复刻原版 HeroesList.java）：
/// 下拉选择英雄 → 立绘 + 属性 + 四技能说明；下拉选择物品 → 图标 + 价格 + 说明。
/// 数据全部来自本地目录（Zytter.Core/Data/*.json），无需服务器。
/// </summary>
public partial class HeroesList : Window
{
    private readonly GameDataCatalog _catalog = GameDataCatalog.LoadDefault();

    private OptionButton _heroSelect = null!;
    private OptionButton _itemSelect = null!;
    private TextureRect _heroPortrait = null!;
    private Label _heroName = null!;
    private Label _heroStats = null!;
    private RichTextLabel _heroSkills = null!;
    private TextureRect _itemIcon = null!;
    private Label _itemName = null!;
    private Label _itemMeta = null!;
    private Label _itemDesc = null!;

    public override void _Ready()
    {
        _heroSelect = GetNode<OptionButton>("%HeroSelect");
        _itemSelect = GetNode<OptionButton>("%ItemSelect");
        _heroPortrait = GetNode<TextureRect>("%HeroPortrait");
        _heroName = GetNode<Label>("%HeroName");
        _heroStats = GetNode<Label>("%HeroStats");
        _heroSkills = GetNode<RichTextLabel>("%HeroSkills");
        _itemIcon = GetNode<TextureRect>("%ItemIcon");
        _itemName = GetNode<Label>("%ItemName");
        _itemMeta = GetNode<Label>("%ItemMeta");
        _itemDesc = GetNode<Label>("%ItemDesc");

        GetNode<Button>("%Close").Pressed += () => EmitSignal(SignalName.CloseRequested);

        // 英雄下拉
        _heroSelect.AddItem("请选择英雄", -1);
        foreach (var hero in _catalog.Heroes.Values.OrderBy(h => h.Id))
            _heroSelect.AddItem(hero.Name, hero.Id);
        _heroSelect.ItemSelected += index => ShowHero((int)_heroSelect.GetItemId((int)index));

        // 物品下拉
        _itemSelect.AddItem("请选择物品", -1);
        foreach (var item in _catalog.Items.Values.OrderBy(i => i.Id))
            _itemSelect.AddItem(item.Name, item.Id);
        _itemSelect.ItemSelected += index => ShowItem((int)_itemSelect.GetItemId((int)index));
    }

    private void ShowHero(int heroId)
    {
        if (heroId <= 0)
        {
            _heroName.Text = "请选择英雄";
            _heroStats.Text = "";
            _heroSkills.Text = "";
            _heroPortrait.Texture = null;
            return;
        }

        var hero = _catalog.GetHero(heroId);

        string portrait = Net.HeroPortrait(heroId);
        _heroPortrait.Texture = ResourceLoader.Exists(portrait) ? GD.Load<Texture2D>(portrait) : null;

        _heroName.Text = $"{hero.Name}（{hero.Ename}）";
        _heroStats.Text = $"生命 {hero.Hp} ｜ 魔法 {hero.Mp}\n" +
                          $"攻击 {hero.Atk} ｜ 护甲 {hero.Def} ｜ 魔抗 {hero.Adf}\n" +
                          $"行动力 {hero.Move} ｜ 每回合回蓝 {hero.Remp}\n" +
                          (hero.Crystal ? "✨ 可激活结晶之力（上场 5 回合或累计 20 魔法伤害）" : "");

        var skills = new System.Text.StringBuilder();
        foreach (var slot in new[] { SkillSlot.Q, SkillSlot.W, SkillSlot.E, SkillSlot.R })
        {
            var skill = _catalog.GetSkill(hero, slot);
            if (skill is null) continue;
            string icon = Net.SkillIcon(heroId, slot);
            string iconText = ResourceLoader.Exists(icon) ? "[img]" + icon + "[/img] " : $"[b]{slot}[/b] ";
            skills.AppendLine(iconText + $"{skill.Name}（{skill.Mp} 蓝）");
            skills.AppendLine($"[color=#b9bcc9]{UiHelpers.Wrap(skill.Describe, 34)}[/color]");
            skills.AppendLine();
        }
        _heroSkills.Text = skills.ToString();
    }

    private void ShowItem(int itemId)
    {
        if (itemId <= 0)
        {
            _itemName.Text = "请选择物品";
            _itemMeta.Text = "";
            _itemDesc.Text = "";
            _itemIcon.Texture = null;
            return;
        }

        var item = _catalog.GetItem(itemId);

        string icon = Net.ItemIcon(itemId);
        _itemIcon.Texture = ResourceLoader.Exists(icon) ? GD.Load<Texture2D>(icon) : null;

        _itemName.Text = item.Name;
        string kind = item.Kind == ItemKind.Consumable ? "消耗品" : "装备";
        _itemMeta.Text = $"{item.Gold} 金币 ｜ {kind}";
        _itemDesc.Text = UiHelpers.Wrap(item.Describe, 30);
    }
}
