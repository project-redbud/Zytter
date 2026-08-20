using Microsoft.EntityFrameworkCore;
using Zytter.Core.Common;
using Zytter.Server.Auth;
using Zytter.Server.Persistence;

namespace Zytter.Server.Features.Accounts;

/// <summary>账户服务：注册/登录（PBKDF2 哈希，修复旧版明文密码）。</summary>
public sealed class AccountService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly TokenService _tokens;

    public AccountService(IDbContextFactory<AppDbContext> dbFactory, TokenService tokens)
    {
        _dbFactory = dbFactory;
        _tokens = tokens;
    }

    public sealed record AuthResult(bool Success, string? Error = null, string? Token = null, long? AccountId = null, string? Username = null);

    public async Task<AuthResult> RegisterAsync(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || username.Length is < 2 or > 16)
            return new AuthResult(false, "用户名长度需为 2~16 个字符");
        if (string.IsNullOrWhiteSpace(password) || password.Length is < 6 or > 32)
            return new AuthResult(false, "密码长度需为 6~32 个字符");

        await using var db = await _dbFactory.CreateDbContextAsync();
        if (await db.Accounts.AnyAsync(a => a.Username == username))
            return new AuthResult(false, "该用户名已被占用");

        var account = new Account
        {
            Username = username,
            PasswordHash = PasswordHasher.Hash(password),
        };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();

        return new AuthResult(true, Token: _tokens.Issue(account.Id), AccountId: account.Id, Username: account.Username);
    }

    public async Task<AuthResult> LoginAsync(string username, string password)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Username == username);
        if (account is null || !PasswordHasher.Verify(password, account.PasswordHash))
            return new AuthResult(false, "用户名或密码错误");

        return new AuthResult(true, Token: _tokens.Issue(account.Id), AccountId: account.Id, Username: account.Username);
    }

    /// <summary>按令牌取账户（无效返回 null）。</summary>
    public async Task<Account?> GetByTokenAsync(string token)
    {
        long? accountId = _tokens.Validate(token);
        if (accountId is null) return null;
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId.Value);
    }

    /// <summary>修改用户名（账号信息界面，原版 Personal 的"修改用户名"）。</summary>
    public async Task<AuthResult> ChangeUsernameAsync(string token, string newUsername)
    {
        long? accountId = _tokens.Validate(token);
        if (accountId is null) return new AuthResult(false, "无效的登录凭证");
        if (string.IsNullOrWhiteSpace(newUsername) || newUsername.Length is < 2 or > 16 || newUsername.Contains(' '))
            return new AuthResult(false, "用户名需 2~16 个字符且不含空格");

        await using var db = await _dbFactory.CreateDbContextAsync();
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId.Value);
        if (account is null) return new AuthResult(false, "无效的登录凭证");
        if (await db.Accounts.AnyAsync(a => a.Username == newUsername && a.Id != accountId.Value))
            return new AuthResult(false, "该用户名已被占用");

        account.Username = newUsername;
        await db.SaveChangesAsync();
        return new AuthResult(true, Username: newUsername);
    }

    /// <summary>修改密码（校验原密码，PBKDF2 重新哈希）。</summary>
    public async Task<AuthResult> ChangePasswordAsync(string token, string oldPassword, string newPassword)
    {
        long? accountId = _tokens.Validate(token);
        if (accountId is null) return new AuthResult(false, "无效的登录凭证");
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length is < 6 or > 32)
            return new AuthResult(false, "新密码长度需为 6~32 个字符");

        await using var db = await _dbFactory.CreateDbContextAsync();
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId.Value);
        if (account is null) return new AuthResult(false, "无效的登录凭证");
        if (!PasswordHasher.Verify(oldPassword, account.PasswordHash))
            return new AuthResult(false, "原密码错误");

        account.PasswordHash = PasswordHasher.Hash(newPassword);
        await db.SaveChangesAsync();
        return new AuthResult(true);
    }
}
