using System.Net.Http.Json;
using System.Text.Json;
using Godot;

namespace Zytter.Client;

/// <summary>
/// 赛季数据（复刻原版 Season.java 双栏布局）：左侧赛季天梯排行榜 + 统计时间，
/// 右侧 [版本] 服务器名 / 赛季时间 / 当前赛季数据（Elo/Rank/定级 + 场次胜败胜率）。
/// 以模态窗（Window）形式由主界面弹出，关闭即释放，不切换场景。
/// </summary>
public partial class Season : Window
{
    private Tree _board = null!;
    private Label _updatedAt = null!;
    private Label _serverTag = null!;
    private Label _seasonTime = null!;
    private Label _username = null!;
    private Label _eloRow = null!;
    private Label _rankRow = null!;
    private Label _placeRow = null!;
    private Label _gamesRow = null!;
    private Label _winRow = null!;
    private Label _loseRow = null!;
    private Label _rateRow = null!;
    private Label _status = null!;

    public override void _Ready()
    {
        _board = GetNode<Tree>("%Board");
        _updatedAt = GetNode<Label>("%UpdatedAt");
        _serverTag = GetNode<Label>("%ServerTag");
        _seasonTime = GetNode<Label>("%SeasonTime");
        _username = GetNode<Label>("%Username");
        _eloRow = GetNode<Label>("%EloRow");
        _rankRow = GetNode<Label>("%RankRow");
        _placeRow = GetNode<Label>("%PlaceRow");
        _gamesRow = GetNode<Label>("%GamesRow");
        _winRow = GetNode<Label>("%WinRow");
        _loseRow = GetNode<Label>("%LoseRow");
        _rateRow = GetNode<Label>("%RateRow");
        _status = GetNode<Label>("%Status");
        // 关闭模态窗：发射 close_requested，由主界面负责释放（Esc/标题栏 X 也走同一路径）
        GetNode<Button>("%Back").Pressed += () => EmitSignal(SignalName.CloseRequested);
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        _status.Text = "正在加载赛季数据……";
        try
        {
            using var http = new System.Net.Http.HttpClient { BaseAddress = new Uri(Net.Instance.ServerUrl) };

            var boardTask = http.GetFromJsonAsync<JsonElement>("/season/top");
            var meTask = http.GetFromJsonAsync<JsonElement>($"/season/me?token={Uri.EscapeDataString(Net.Instance.Token)}");
            var infoTask = http.GetFromJsonAsync<JsonElement>("/info");
            await Task.WhenAll(boardTask, meTask, infoTask);

            // 窗体可能已被用户关闭，避免对已释放节点赋值
            if (IsQueuedForDeletion()) return;

            var board = await boardTask;
            var me = await meTask;
            var info = await infoTask;

            _serverTag.Text = $"[{info.GetProperty("version").GetString()}] {info.GetProperty("name").GetString()}";
            _seasonTime.Text = $"赛季时间：{info.GetProperty("seasonTime").GetString()}";
            _updatedAt.Text = $"数据统计截至于：{DateTime.Now:yyyy-MM-dd HH:mm:ss}";

            // 左侧排行榜：表格（Tree 六列）
            _board.Columns = 6;
            _board.HideRoot = true;
            _board.SetColumnTitle(0, "排名");
            _board.SetColumnTitle(1, "玩家");
            _board.SetColumnTitle(2, "Elo");
            _board.SetColumnTitle(3, "段位");
            _board.SetColumnTitle(4, "场次");
            _board.SetColumnTitle(5, "胜率");
            _board.Clear();

            void AddRows(string prefix)
            {
                var source = prefix == "top" ? board.GetProperty("top") : board.GetProperty("reserve");
                foreach (var row in source.EnumerateArray())
                {
                    var item = _board.CreateItem();
                    item.SetText(0, row.GetProperty("position").GetString());
                    item.SetText(1, row.GetProperty("username").GetString());
                    item.SetText(2, row.GetProperty("elo").GetInt32().ToString());
                    item.SetText(3, row.GetProperty("rank").GetString());
                    item.SetText(4, row.GetProperty("games").GetInt32().ToString());
                    item.SetText(5, $"{row.GetProperty("winRate").GetDouble()}%");
                }
            }
            AddRows("top");
            AddRows("reserve");

            // 右侧当前赛季数据
            int elo = me.GetProperty("elo").GetInt32();
            int best = me.GetProperty("bestElo").GetInt32();
            int placements = me.GetProperty("placementsLeft").GetInt32();
            int wins = me.GetProperty("wins").GetInt32();
            int losses = me.GetProperty("losses").GetInt32();
            int games = wins + losses;
            double winRate = me.GetProperty("winRate").GetDouble();

            _username.Text = me.GetProperty("username").GetString() ?? "";
            _eloRow.Text = placements > 0 ? $"Elo：暂无（Best：{best}）" : $"Elo：{elo}（Best：{best}）";
            _rankRow.Text = placements > 0 ? "Rank：未定级" : $"Rank：{me.GetProperty("rank").GetString()}";
            _placeRow.Text = placements > 0 ? $"定级赛：还需胜利 {placements} 场才能激活" : "定级赛：已激活";
            _gamesRow.Text = $"赛季场次：{games}";
            _winRow.Text = $"赛季胜场：{wins}";
            _loseRow.Text = $"赛季败场：{losses}";
            _rateRow.Text = $"赛季胜率：{winRate}%";

            _status.Text = "";
        }
        catch (Exception ex)
        {
            _status.Text = $"加载失败：{ex.Message}";
        }
    }
}
