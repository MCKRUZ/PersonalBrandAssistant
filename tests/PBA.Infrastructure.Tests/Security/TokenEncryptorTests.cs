using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Security;
using Xunit;

namespace PBA.Infrastructure.Tests.Security;

public class TokenEncryptorTests
{
    private static string GenerateKey() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static TokenEncryptor CreateEncryptor(string? key = null) =>
        new(Options.Create(new EncryptionOptions { Key = key ?? GenerateKey() }));

    [Fact]
    public void Encrypt_ThenDecrypt_ReturnsOriginalValue()
    {
        var encryptor = CreateEncryptor();
        var plaintext = "my-secret-token";

        var ciphertext = encryptor.Encrypt(plaintext);
        var decrypted = encryptor.Decrypt(ciphertext);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encrypt_ProducesDifferentCiphertextEachTime()
    {
        var encryptor = CreateEncryptor();
        var plaintext = "same-token";

        var cipher1 = encryptor.Encrypt(plaintext);
        var cipher2 = encryptor.Encrypt(plaintext);

        Assert.NotEqual(cipher1, cipher2);
    }

    [Fact]
    public void Decrypt_WithWrongKey_ThrowsCryptographicException()
    {
        var encryptor1 = CreateEncryptor();
        var encryptor2 = CreateEncryptor();

        var ciphertext = encryptor1.Encrypt("secret");

        Assert.ThrowsAny<CryptographicException>(() => encryptor2.Decrypt(ciphertext));
    }

    [Fact]
    public void Encrypt_NullInput_ThrowsArgumentNullException()
    {
        var encryptor = CreateEncryptor();

        Assert.Throws<ArgumentNullException>(() => encryptor.Encrypt(null!));
    }

    [Fact]
    public void Decrypt_CorruptedCiphertext_ThrowsCryptographicException()
    {
        var encryptor = CreateEncryptor();
        var ciphertext = encryptor.Encrypt("secret");

        var bytes = Convert.FromBase64String(ciphertext);
        bytes[bytes.Length / 2] ^= 0xFF;
        var corrupted = Convert.ToBase64String(bytes);

        Assert.ThrowsAny<CryptographicException>(() => encryptor.Decrypt(corrupted));
    }

    [Fact]
    public void Encrypt_ThenDecrypt_HandlesEmptyString()
    {
        var encryptor = CreateEncryptor();

        var ciphertext = encryptor.Encrypt("");
        var decrypted = encryptor.Decrypt(ciphertext);

        Assert.Equal("", decrypted);
    }

    [Fact]
    public void Encrypt_ThenDecrypt_HandlesLongTokens()
    {
        var encryptor = CreateEncryptor();
        var longToken = new string('x', 4096);

        var ciphertext = encryptor.Encrypt(longToken);
        var decrypted = encryptor.Decrypt(ciphertext);

        Assert.Equal(longToken, decrypted);
    }
}
