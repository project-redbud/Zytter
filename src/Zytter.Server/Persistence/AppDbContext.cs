using Microsoft.EntityFrameworkCore;

namespace Zytter.Server.Persistence;

/// <summary>
/// Zytter 服务器数据库上下文（EF Core Code-First + SQLite）。
/// 对局中的临时状态（战斗/金币/道具盒）只存在于内存的 BattleSession，
/// 数据库只持久化账户、英雄/技能/物品养成、战绩与赛季。
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<HeroOwnership> HeroOwnerships => Set<HeroOwnership>();
    public DbSet<MatchRecord> MatchRecords => Set<MatchRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Account>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Username).HasMaxLength(16).IsRequired();
            e.HasIndex(x => x.Username).IsUnique();
            e.Property(x => x.PasswordHash).HasMaxLength(256).IsRequired();
            e.Property(x => x.Elo).HasDefaultValue(1200);
        });

        modelBuilder.Entity<HeroOwnership>(e =>
        {
            e.HasKey(x => new { x.AccountId, x.HeroId });
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MatchRecord>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.WinnerAccountId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.LoserAccountId).OnDelete(DeleteBehavior.SetNull);
        });
    }
}
