using System.Text.Json;
using Godot;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Zytter.Core.Data;

namespace Zytter.Client;

/// <summary>
/// 网络 + 音频单例（Autoload）：持有大厅/对局/聊天三条 SignalR 连接、账户会话与 BGM/音效播放。
/// 旧版客户端 5 条 TCP 长连接 + 明文密码序列化；新版 3 条 Hub 连接 + Bearer 令牌。
/// </summary>
public partial class Net : Node
{
    public static Net Instance { get; private set; } = null!;

    public string ServerUrl { get; set; } = "http://127.0.0.1:17717";

    public string Token { get; set; } = "";
    public string Username { get; set; } = "";

    public HubConnection? Lobby { get; private set; }
    public HubConnection? Battle { get; private set; }
    public HubConnection? Chat { get; private set; }

    /// <summary>匹配成功后的对局信息。</summary>
    public Guid RoomId { get; set; }

    public string Side { get; set; } = "A";

    /// <summary>本方出战英雄名单（由 B/P 禁选结果决定）。</summary>
    public int[] Roster { get; set; } = { 1, 2, 3 };

    /// <summary>机器人模式：--bot 命令行参数启用（无头自动对战测试用）。</summary>
    public bool IsBot { get; set; }

    /// <summary>BGM/音效开关：--nobgm（或原版 -bgmoff）参数关闭；无头机器人模式自动关闭。</summary>
    public bool BgmEnabled { get; set; } = true;

    // ==================== 音频（复刻原版 BGM.java 播放逻辑） ====================

    private AudioStreamPlayer _bgm = null!;
    private readonly AudioStreamPlayer[] _sfx = new AudioStreamPlayer[4];
    private int _sfxIndex;

    /// <summary>BGM 模式：lobby=main1↔main2 轮换 / draft=heroselect 循环 / battle=fight1~3 随机轮换 / 其他=单曲循环。</summary>
    private string _bgmMode = "";
    private bool _mainTrackToggle;
    private int _fightTrack; // 0=fight1 1=fight2 2=fight3

    public override void _Ready()
    {
        Instance = this;

        // 命令行参数：--nobgm / -bgmoff 关闭音频；--bot 进入无头机器人模式
        var args = OS.GetCmdlineUserArgs();
        BgmEnabled = !args.Contains("--nobgm") && !args.Contains("-bgmoff") && !args.Contains("--bot");

        _bgm = new AudioStreamPlayer { Bus = "Master" };
        AddChild(_bgm);
        _bgm.Finished += OnBgmFinished;
        for (int i = 0; i < _sfx.Length; i++)
        {
            _sfx[i] = new AudioStreamPlayer { Bus = "Master" };
            AddChild(_sfx[i]);
        }

        LoadSession();
    }

    // ==================== 会话持久化（记住登录状态） ====================

    private const string SessionPath = "user://session.json";

    private void LoadSession()
    {
        try
        {
            if (!Godot.FileAccess.FileExists(SessionPath)) return;
            using var file = Godot.FileAccess.Open(SessionPath, Godot.FileAccess.ModeFlags.Read);
            var json = Json.ParseString(file.GetAsText()).AsGodotDictionary();
            ServerUrl = json.ContainsKey("serverUrl") ? (string)json["serverUrl"] : ServerUrl;
            Token = json.ContainsKey("token") ? (string)json["token"] : "";
            Username = json.ContainsKey("username") ? (string)json["username"] : "";
        }
        catch (Exception ex)
        {
            GD.PrintErr($"读取会话失败：{ex.Message}");
        }
    }

    /// <summary>登录/注册成功后保存会话（游戏结束回主界面不掉登录，重启进程也能恢复）。</summary>
    public void SaveSession()
    {
        try
        {
            var dict = new Godot.Collections.Dictionary
            {
                ["serverUrl"] = ServerUrl,
                ["token"] = Token,
                ["username"] = Username,
            };
            using var file = Godot.FileAccess.Open(SessionPath, Godot.FileAccess.ModeFlags.Write);
            file.StoreString(Json.Stringify(dict));
        }
        catch (Exception ex)
        {
            GD.PrintErr($"保存会话失败：{ex.Message}");
        }
    }

    private static AudioStream? LoadAudio(string file)
    {
        var path = $"res://assets/bgm/{file}";
        return ResourceLoader.Exists(path) ? GD.Load<AudioStream>(path) : null;
    }

    private void PlayBgmFile(string file)
    {
        if (!BgmEnabled) return;
        var stream = LoadAudio($"{file}.mp3");
        if (stream is null) return;
        _bgm.Stream = stream;
        _bgm.Play();
    }

    /// <summary>大厅 BGM：main1 → main2 → main1 循环（原版 playerlist 0/1 互切）。
    /// 幂等：若当前已在循环播放大厅 BGM，则不再从头重播（避免从其它界面返回主菜单时重播）。</summary>
    public void PlayLobbyBgm()
    {
        if (_bgmMode == "lobby" && _bgm.Playing) return;
        _bgmMode = "lobby";
        _mainTrackToggle = false;
        PlayBgmFile("main1");
    }

    /// <summary>禁选 BGM：heroselect 单曲循环（原版 setBGM(2)）。</summary>
    public void PlayDraftBgm()
    {
        _bgmMode = "draft";
        PlayBgmFile("heroselect");
    }

    /// <summary>战斗 BGM：随机 fight1/2/3 起手，结束后按原版概率轮换（不按顺序、可随机重复）。</summary>
    public void PlayBattleBgm()
    {
        _bgmMode = "battle";
        _fightTrack = GD.RandRange(0, 2);
        PlayBgmFile(FightFileName(_fightTrack));
    }

    /// <summary>单曲循环 BGM（lastbattle/lastonehero/willwin/win/lose）。</summary>
    public void PlayLoopBgm(string name)
    {
        _bgmMode = name;
        PlayBgmFile(name);
    }

    private static string FightFileName(int track) => track switch
    {
        0 => "fight1",
        1 => "fight2",
        _ => "fight3",
    };

    private void OnBgmFinished()
    {
        if (!BgmEnabled) return;
        switch (_bgmMode)
        {
            case "lobby":
                // main1 → main2 → main1 循环
                _mainTrackToggle = !_mainTrackToggle;
                PlayBgmFile(_mainTrackToggle ? "main2" : "main1");
                break;

            case "battle":
                // 原版轮换规则（BGM.java case 3/4/5）：
                // fight1：2/3 概率切 fight2，1/3 切 fight3（不重复 fight1）
                // fight2：1/3 切 fight1，1/3 重复 fight2，1/3 切 fight3
                // fight3：1/3 切 fight1，1/3 切 fight2，1/3 重复 fight3
                int b = GD.RandRange(0, 2);
                _fightTrack = _fightTrack switch
                {
                    0 => b == 2 ? 2 : 1,
                    1 => b,
                    _ => b == 2 ? 2 : b,
                };
                PlayBgmFile(FightFileName(_fightTrack));
                break;

            default:
                // 单曲循环（heroselect/lastbattle/lastonehero/willwin/win/lose）
                PlayBgmFile(_bgmMode);
                break;
        }
    }

    public void StopBgm() => _bgm.Stop();

    /// <summary>播放一次性音效（atk/magic/dead/kill/win/lose/gamematchisready/startmatch）。</summary>
    public void PlaySfx(string name)
    {
        if (!BgmEnabled) return;
        var stream = LoadAudio($"{name}.mp3");
        if (stream is null) return;
        var player = _sfx[_sfxIndex];
        _sfxIndex = (_sfxIndex + 1) % _sfx.Length;
        player.Stream = stream;
        player.Play();
    }

    // ==================== 连接 ====================

    public static HubConnection BuildConnection(string serverUrl, string hub)
    {
        return new HubConnectionBuilder()
            .WithUrl($"{serverUrl}/hubs/{hub}", options => options.Transports = HttpTransportType.WebSockets)
            .AddJsonProtocol(options =>
                options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
            .Build();
    }

    public async Task EnsureLobbyAsync()
    {
        if (Lobby is not null) return;
        Lobby = BuildConnection(ServerUrl, "lobby");
        Lobby.Closed += ex => { GD.PrintErr($"大厅连接断开：{ex?.Message}"); return Task.CompletedTask; };
        await Lobby.StartAsync();
    }

    public async Task EnsureBattleAsync()
    {
        if (Battle is not null) return;
        Battle = BuildConnection(ServerUrl, "battle");
        Battle.Closed += ex => { GD.PrintErr($"对局连接断开：{ex?.Message}"); return Task.CompletedTask; };
        await Battle.StartAsync();
    }

    public async Task EnsureChatAsync()
    {
        if (Chat is not null) return;
        Chat = BuildConnection(ServerUrl, "chat");
        Chat.Closed += ex => { GD.PrintErr($"聊天连接断开：{ex?.Message}"); return Task.CompletedTask; };
        await Chat.StartAsync();
    }

    // ==================== 静态资源工具 ====================

    /// <summary>英雄头像（大图）路径。</summary>
    public static string HeroPortrait(int heroId)
    {
        var catalog = GameDataCatalog.LoadDefault();
        return $"res://assets/heroes/{catalog.GetHero(heroId).Ename}.jpg";
    }

    /// <summary>禁选小头像路径。</summary>
    public static string HeroSelect(int heroId)
    {
        var catalog = GameDataCatalog.LoadDefault();
        return $"res://assets/selects/{catalog.GetHero(heroId).Ename}.jpg";
    }

    /// <summary>技能图标路径（无该技能返回空串）。</summary>
    public static string SkillIcon(int heroId, SkillSlot slot)
    {
        var catalog = GameDataCatalog.LoadDefault();
        var hero = catalog.GetHero(heroId);
        var skill = catalog.GetSkill(hero, slot);
        if (skill is null) return "";
        string file = slot.ToString().ToLower();
        return $"res://assets/skills/{hero.Ename}/{file}.png";
    }

    /// <summary>
    /// 道具图标路径（原版 Store.java 的图标映射，按物品 ID）。
    /// 二阶红月神杖（27）复用红月神杖图标。
    /// </summary>
    public static string ItemIcon(int itemId)
    {
        var file = itemId switch
        {
            1 => "ys",       // 回合延时
            2 => "hp1",      // 回复药
            3 => "hp2",      // 中回复药
            4 => "hp3",      // 大回复药
            5 => "fs1",      // 复苏胶囊
            6 => "fs2",      // 高级复苏胶囊
            7 => "mp1",      // 魔力填充剂I
            8 => "mp2",      // 魔力填充剂II
            9 => "mp3",      // 魔力填充剂III
            10 => "xdl",     // 行动力胶囊
            11 => "skys",    // 双抗药贴
            12 => "qhys",    // 强化药水
            13 => "zysz",    // 紫月神杖
            14 => "hysz",    // 红月神杖
            15 => "zzqy",    // 长剑-朝醉青烟
            16 => "sofa",    // 鹰角弓
            17 => "lszbs",   // 狩猎者匕首
            18 => "xinye",   // 新叶传教者手札
            19 => "pjzm",    // 破军之矛
            20 => "wdln",    // 维多利娜长袍
            21 => "sydp",    // 圣月斗篷
            22 => "jrzzd",   // 坚韧者之盾
            23 => "shzj",    // 守护之戒
            24 => "txj",     // 耐久光环
            25 => "hh",      // 学生会的会徽
            26 => "yyzs",    // 夜宴之声
            27 => "hysz",    // 二阶红月神杖（复用红月图标）
            _ => "",
        };
        if (file.Length == 0) return "";
        string path = $"res://assets/items/{file}.jpg";
        return ResourceLoader.Exists(path) ? path : "";
    }

    /// <summary>Buff 图标路径（按 buff id 映射原版图标；无匹配返回空串）。</summary>
    public static string BuffIcon(string buffId)
    {
        var file = buffId switch
        {
            "liberation" or "cloud_top" or "anthem_atk" or "rift_atk" or "power_potion" => "atkup.png",
            "exploitation" => "defdown.png",
            "star_fall" => "mdfdown.png",
            "slaughter_wind" or "ap_capsule" => "speedup.png",
            "flash_plus_mdf" or "tide_choice_mdf" or "resist_patch_mdf" => "mdfup.png",
            "tide_choice_def" or "resist_patch_def" or "anthem_def" => "defup.png",
            "first_move" => "appup.png",
            "mp_filler_iii" => "hpadd.png",
            "revival" => "magicimmunity.png",
            "wind_barrier_stun" or "ice_cross" => "allunable.png",
            "wind_barrier_lim" => "limited.png",
            "round_square" or "rift" => "atkunable.png",
            "love_flower_enemy" => "magicunable.png",
            "flash" or "flash_plus" => "evade.png",
            "heart_realm" => "hpadd.png",
            "oracle" => "limited.png",
            _ => "",
        };
        if (file.Length == 0) return "";
        string path = $"res://assets/buffs/{file}";
        return ResourceLoader.Exists(path) ? path : "";
    }
}
