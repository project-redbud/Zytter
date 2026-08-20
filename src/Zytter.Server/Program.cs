using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Zytter.Server.Auth;
using Zytter.Server.Features.Accounts;
using Zytter.Server.Features.Ai;
using Zytter.Server.Features.Battle;
using Zytter.Server.Features.Drafting;
using Zytter.Server.Features.Matchmaking;
using Zytter.Server.Features.Season;
using Zytter.Server.Hubs;
using Zytter.Server.Persistence;

var builder = WebApplication.CreateBuilder(args);

// 持久化：SQLite + EF Core（工厂模式，服务自行管理生命周期）
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("zytter")));

// 领域服务
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<AccountService>();
builder.Services.AddSingleton<Matchmaker>();
builder.Services.AddSingleton<MatchConfirmRegistry>();
builder.Services.AddSingleton<MatchConfirmHost>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MatchConfirmHost>());
builder.Services.AddSingleton<BattleRegistry>();
builder.Services.AddSingleton<DraftRegistry>();
builder.Services.AddSingleton<MatchRecorder>();
builder.Services.AddSingleton<AiDriver>();
builder.Services.AddHostedService<BattleLoopHost>();
// DraftLoopHost 既是被 Hub 注入的服务，也是后台循环
builder.Services.AddSingleton<DraftLoopHost>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DraftLoopHost>());

// SignalR（JSON 协议，camelCase；Core 事件带 $type 多态判别）
builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    });

builder.Services.AddHealthChecks();

var app = builder.Build();

// 启动时迁移数据库（自托管场景零手工步骤）
await using (var scope = app.Services.CreateAsyncScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    await using var db = await factory.CreateDbContextAsync();
    await db.Database.MigrateAsync();
}

app.MapHealthChecks("/health");

// 服务器信息（主界面 "[版本] 服务器名" 状态栏，复刻原版 Config.dig；赛季时间供赛季界面展示）
app.MapGet("/info", (IConfiguration config) => Results.Ok(new
{
    Name = "红芽计划 · 学园激斗事件簿",
    Version = "2.0-remake",
    SeasonTime = config["Season:Time"] ?? "当前赛季进行中",
}));

app.MapHub<LobbyHub>("/hubs/lobby");
app.MapHub<BattleHub>("/hubs/battle");
app.MapHub<ChatHub>("/hubs/chat");

// 战绩查询（MVP：最近 10 场；后续接入客户端战绩界面与鉴权）
app.MapGet("/records/latest", async (IDbContextFactory<AppDbContext> factory) =>
{
    await using var db = await factory.CreateDbContextAsync();
    var records = await db.MatchRecords
        .OrderByDescending(r => r.Id)
        .Take(10)
        .Select(r => new
        {
            r.Id,
            r.CreatedAt,
            r.WinnerAccountId,
            r.LoserAccountId,
            r.Rounds,
            r.WinnerKills,
            r.LoserKills,
            r.Reason,
        })
        .ToListAsync();
    return Results.Ok(records);
});

// 赛季天梯排行：TOP1~10 + 候补委员（未定级者 Elo 视为 0 排最后）
app.MapGet("/season/top", async (IDbContextFactory<AppDbContext> factory) =>
{
    await using var db = await factory.CreateDbContextAsync();
    var accounts = await db.Accounts.ToListAsync();

    var ranked = accounts
        .Select(a => new
        {
            a.Username,
            a.Elo,
            a.Wins,
            a.Losses,
            a.PlacementsLeft,
            Rank = SeasonCalculator.RankFor(a.Elo, a.PlacementsLeft),
            Games = a.Wins + a.Losses,
            WinRate = a.Wins + a.Losses > 0 ? (double)a.Wins / (a.Wins + a.Losses) : 0,
            Placed = SeasonCalculator.IsPlaced(a.PlacementsLeft),
        })
        .OrderByDescending(x => x.Placed)
        .ThenByDescending(x => x.Elo)
        .ToList();

    var top = ranked.Take(10)
        .Select((x, i) => new { Position = $"TOP{i + 1}", x.Username, x.Elo, x.Rank, x.Games, x.Wins, WinRate = Math.Round(x.WinRate * 100, 1) })
        .ToList();
    var reserve = ranked.Skip(10).Take(1)
        .Select(x => new { Position = "候补委员", x.Username, x.Elo, x.Rank, x.Games, x.Wins, WinRate = Math.Round(x.WinRate * 100, 1) })
        .ToList();

    return Results.Ok(new { Top = top, Reserve = reserve });
});

// 本人赛季数据（Elo/Best/Rank/场次/胜率）
app.MapGet("/season/me", async (string token, IDbContextFactory<AppDbContext> factory, TokenService tokens) =>
{
    long? accountId = tokens.Validate(token);
    if (accountId is null) return Results.Unauthorized();
    await using var db = await factory.CreateDbContextAsync();
    var a = await db.Accounts.FindAsync(accountId.Value);
    if (a is null) return Results.NotFound();

    int games = a.Wins + a.Losses;
    double winRate = games > 0 ? (double)a.Wins / games : 0;
    return Results.Ok(new
    {
        Id = a.Id,
        a.Username,
        a.Elo,
        a.BestElo,
        Rank = SeasonCalculator.RankFor(a.Elo, a.PlacementsLeft),
        a.PlacementsLeft,
        a.Wins,
        a.Losses,
        Games = games,
        WinRate = Math.Round(winRate * 100, 1),
    });
});

app.Run();
