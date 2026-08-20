using System.Security.Cryptography;
using System.Text;

namespace Zytter.Server.Auth;

/// <summary>
/// Bearer 令牌：HMAC-SHA256 签名（无状态，重启后依然有效，修复"自动登录 401"）。
/// 载荷 = 账户ID.签发时间戳；密钥来自配置 Tokens:Secret（默认值仅用于开发）。
/// </summary>
public sealed class TokenService
{
    private readonly byte[] _key;
    private readonly TimeSpan _lifetime = TimeSpan.FromDays(30);

    public TokenService(IConfiguration configuration)
    {
        string? secret = configuration["Tokens:Secret"];
        _key = string.IsNullOrEmpty(secret)
            ? RandomNumberGenerator.GetBytes(32)
            : SHA256.HashData(Encoding.UTF8.GetBytes(secret));
    }

    /// <summary>签发新令牌并绑定账户。</summary>
    public string Issue(long accountId)
    {
        string payload = $"{accountId}.{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        return Base64Url(payload) + "." + Base64UrlBytes(Sign(payload));
    }

    /// <summary>校验令牌并返回账户 ID（签名无效或过期返回 null）。</summary>
    public long? Validate(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 2) return null;

            string payload = DecodeBase64Url(parts[0]);
            byte[] sig = DecodeBytes(parts[1]);
            if (!CryptographicOperations.FixedTimeEquals(Sign(payload), sig)) return null;

            var fields = payload.Split('.');
            if (fields.Length != 2) return null;
            long accountId = long.Parse(fields[0]);
            long issued = long.Parse(fields[1]);
            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - issued > _lifetime.TotalSeconds) return null;
            return accountId;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>无状态令牌无法真正吊销；保留接口以兼容（调用方忽略即可）。</summary>
    public void Revoke(string token)
    {
    }

    private byte[] Sign(string payload) =>
        HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(payload));

    private static string Base64Url(string raw) =>
        Base64UrlBytes(Encoding.UTF8.GetBytes(raw));

    private static string Base64UrlBytes(byte[] data) =>
        Convert.ToBase64String(data).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static string DecodeBase64Url(string part) =>
        Encoding.UTF8.GetString(DecodeBytes(part));

    private static byte[] DecodeBytes(string part)
    {
        string b64 = part.Replace('-', '+').Replace('_', '/');
        b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
        return Convert.FromBase64String(b64);
    }
}
