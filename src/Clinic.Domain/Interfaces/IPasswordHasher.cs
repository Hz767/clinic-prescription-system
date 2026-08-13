namespace Clinic.Domain.Interfaces;

/// <summary>
/// 密码哈希接口。
/// v1 使用 PBKDF2-HMAC-SHA256（迭代 ≥100,000）；
/// P1 升级目标为 Argon2id。
/// </summary>
public interface IPasswordHasher
{
    /// <summary>哈希密码，返回存储格式字符串</summary>
    string Hash(string password);

    /// <summary>验证密码是否匹配哈希</summary>
    bool Verify(string password, string hash);
}
