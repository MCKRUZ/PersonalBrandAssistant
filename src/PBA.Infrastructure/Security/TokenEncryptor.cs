using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Infrastructure.Configuration;

namespace PBA.Infrastructure.Security;

public sealed class TokenEncryptor : ITokenEncryptor
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;

    public TokenEncryptor(IOptions<EncryptionOptions> options)
    {
        _key = Convert.FromBase64String(options.Value.Key);
        if (_key.Length != 32)
            throw new ArgumentException(
                $"Encryption key must be exactly 32 bytes (256-bit). Got {_key.Length} bytes.",
                nameof(options));
    }

    public string Encrypt(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        var result = new byte[NonceSize + ciphertext.Length + TagSize];
        nonce.CopyTo(result, 0);
        ciphertext.CopyTo(result, NonceSize);
        tag.CopyTo(result, NonceSize + ciphertext.Length);

        return Convert.ToBase64String(result);
    }

    public string Decrypt(string ciphertext)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);

        var encryptedBytes = Convert.FromBase64String(ciphertext);

        var nonce = encryptedBytes[..NonceSize];
        var tag = encryptedBytes[^TagSize..];
        var ciphertextBytes = encryptedBytes[NonceSize..^TagSize];
        var plaintext = new byte[ciphertextBytes.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, ciphertextBytes, tag, plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }
}
