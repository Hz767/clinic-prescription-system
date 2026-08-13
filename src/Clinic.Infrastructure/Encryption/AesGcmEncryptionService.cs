using System.Security.Cryptography;
using System.Text;
using Clinic.Domain.Interfaces;

namespace Clinic.Infrastructure.Encryption;

/// <summary>
/// AES-GCM 字段级加密服务实现。
/// 使用外部提供的 32 字节（256 位）密钥，不再从密码派生，消除固定盐风险。
/// 密钥由 DependencyInjection 从 appsettings.json 读取 Base64 编码值，
/// 或在首次运行时随机生成并持久化到 encryption.key 文件。
///
/// 密文格式：Base64(nonce[12] + tag[16] + ciphertext)
///
/// 同时提供基于 HMAC-SHA256 的手机号哈希，用于唯一索引和快速查找。
/// </summary>
public class AesGcmEncryptionService : IEncryptionService
{
    private const int NonceSize = 12;  // AES-GCM 标准 nonce：96 bit
    private const int TagSize = 16;    // AES-GCM 推荐 tag：128 bit
    private const int KeySize = 32;    // AES-256

    private readonly byte[] _key;

    /// <summary>
    /// 构造函数：直接接收 32 字节密钥。
    /// 密钥应由 DI 容器从配置或密钥文件提供，避免在代码中硬编码。
    /// </summary>
    /// <param name="key">32 字节（256 位）AES 密钥</param>
    /// <exception cref="ArgumentException">密钥长度不为 32 字节时抛出</exception>
    public AesGcmEncryptionService(byte[] key)
    {
        if (key is null || key.Length != KeySize)
            throw new ArgumentException(
                $"AES 密钥必须为 {KeySize} 字节（256 位），实际为 {(key?.Length ?? 0)} 字节",
                nameof(key));

        // 防御性拷贝，避免外部修改影响内部状态
        _key = new byte[KeySize];
        Buffer.BlockCopy(key, 0, _key, 0, KeySize);
    }

    public string Encrypt(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
            return string.Empty;

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        // 拼接 nonce + tag + ciphertext
        var result = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, result, nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, result, nonce.Length + tag.Length, ciphertext.Length);

        return Convert.ToBase64String(result);
    }

    public string Decrypt(string ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext))
            return string.Empty;

        var data = Convert.FromBase64String(ciphertext);

        var nonce = data[..NonceSize];
        var tag = data[NonceSize..(NonceSize + TagSize)];
        var ciphertextBytes = data[(NonceSize + TagSize)..];

        var plaintextBytes = new byte[ciphertextBytes.Length];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, ciphertextBytes, tag, plaintextBytes);

        return Encoding.UTF8.GetString(plaintextBytes);
    }

    /// <summary>
    /// 使用服务器端密钥对手机号进行 HMAC-SHA256 哈希。
    /// 返回小写 Hex 字符串（64 字符），用于唯一索引和快速查找。
    /// 相比纯 SHA-256，HMAC 增加了服务器端密钥因子，防止彩虹表攻击。
    /// </summary>
    public string HashPhone(string phone)
    {
        if (string.IsNullOrEmpty(phone))
            return string.Empty;

        var phoneBytes = Encoding.UTF8.GetBytes(phone.Trim());
        var hash = HMACSHA256.HashData(_key, phoneBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
