using System.Security.Cryptography;
using Clinic.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace Clinic.Infrastructure.Security;

/// <summary>
/// PBKDF2-HMAC-SHA256 密码哈希实现。
/// 迭代次数 600,000（OWASP 2023 推荐），配合服务器端 pepper 增强安全性。
/// P1 升级目标：Argon2id。
///
/// 存储格式：{iterations}.{base64salt}.{base64hash}
/// pepper 在 Hash 和 Verify 时均追加到密码末尾，不持久化在哈希字符串中。
/// </summary>
public class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16;         // 128-bit salt
    private const int HashSize = 32;          // 256-bit hash
    private const int Iterations = 600_000;   // OWASP 2023 推荐

    private readonly string _pepper;
    private readonly ILogger<Pbkdf2PasswordHasher>? _logger;

    /// <summary>
    /// 构造函数。
    /// </summary>
    /// <param name="pepper">服务器端 pepper，追加到密码后再进行 PBKDF2 派生。可为空。</param>
    /// <param name="logger">日志记录器，用于记录哈希算法升级提示。可为空。</param>
    public Pbkdf2PasswordHasher(string? pepper = null, ILogger<Pbkdf2PasswordHasher>? logger = null)
    {
        _pepper = pepper ?? string.Empty;
        _logger = logger;
    }

    public string Hash(string password)
    {
        var salt = new byte[SaltSize];
        RandomNumberGenerator.Fill(salt);

        var hash = Rfc2898DeriveBytes.Pbkdf2(
            ApplyPepper(password), salt, Iterations, HashAlgorithmName.SHA256, HashSize);

        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string storedHash)
    {
        if (string.IsNullOrEmpty(storedHash))
            return false;

        var parts = storedHash.Split('.');
        if (parts.Length != 3)
            return false;

        if (!int.TryParse(parts[0], out var iterations))
            return false;

        byte[] salt, expectedHash;
        try
        {
            salt = Convert.FromBase64String(parts[1]);
            expectedHash = Convert.FromBase64String(parts[2]);
        }
        catch
        {
            return false;
        }

        var actualHash = Rfc2898DeriveBytes.Pbkdf2(
            ApplyPepper(password), salt, iterations, HashAlgorithmName.SHA256, HashSize);

        // 固定时间比较，防止时序攻击
        var verified = CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);

        // 自动升级检测：验证成功后，如果存储的迭代次数低于当前值，记录日志提示
        // 不自动 rehash 以避免事务复杂性，由上层在适当时机触发密码重置
        if (verified && iterations < Iterations)
        {
            _logger?.LogWarning(
                "密码哈希迭代次数过低（存储 {StoredIterations}，当前 {CurrentIterations}），" +
                "建议在用户下次登录时引导修改密码以自动升级哈希强度",
                iterations, Iterations);
        }

        return verified;
    }

    /// <summary>
    /// 将 pepper 追加到密码末尾。
    /// pepper 是服务器端密钥，不存储在数据库中，即使数据库泄露攻击者也无法离线破解。
    /// </summary>
    private string ApplyPepper(string password)
    {
        return _pepper.Length == 0 ? password : password + _pepper;
    }
}
