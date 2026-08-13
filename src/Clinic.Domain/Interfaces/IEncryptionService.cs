namespace Clinic.Domain.Interfaces;

/// <summary>
/// 字段级加密服务接口。
/// 用于手机号、诊断等敏感字段的 AES-GCM 加密/解密。
/// 同时提供基于 HMAC-SHA256 的手机号哈希，用于唯一索引和快速查找。
/// </summary>
public interface IEncryptionService
{
    /// <summary>加密明文，返回 Base64 编码的密文（含 nonce）</summary>
    string Encrypt(string plaintext);

    /// <summary>解密 Base64 编码的密文，返回明文</summary>
    string Decrypt(string ciphertext);

    /// <summary>
    /// 使用服务器端密钥对手机号进行 HMAC-SHA256 哈希。
    /// 返回小写 Hex 字符串（64 字符），用于唯一索引和快速查找，不可逆。
    /// 相比纯 SHA-256，HMAC 增加了服务器端密钥，防止彩虹表攻击。
    /// </summary>
    string HashPhone(string phone);
}
