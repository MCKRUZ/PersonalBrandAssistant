using Microsoft.AspNetCore.DataProtection;
using PersonalBrandAssistant.Infrastructure.Services;

namespace PersonalBrandAssistant.Infrastructure.Tests.Services;

public class EncryptionServiceTests
{
    private readonly EncryptionService _service;

    public EncryptionServiceTests()
    {
        var provider = DataProtectionProvider.Create("TestApp");
        _service = new EncryptionService(provider);
    }

    [Fact]
    public void Encrypt_ReturnsNonEmptyBytes()
    {
        var encrypted = _service.Encrypt("test-secret");

        Assert.NotNull(encrypted);
        Assert.NotEmpty(encrypted);
    }

    [Fact]
    public void Decrypt_ReturnsOriginalPlaintext()
    {
        var plaintext = "my-api-key-12345";
        var encrypted = _service.Encrypt(plaintext);
        var decrypted = _service.Decrypt(encrypted);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encrypt_SamePlaintextTwice_ProducesDifferentCiphertext()
    {
        var plaintext = "same-value";
        var encrypted1 = _service.Encrypt(plaintext);
        var encrypted2 = _service.Encrypt(plaintext);

        Assert.NotEqual(encrypted1, encrypted2);
    }

    [Theory]
    [InlineData("")]
    [InlineData("simple")]
    [InlineData("unicode-🎉-emoji")]
    [InlineData("a-very-long-string-that-goes-on-and-on-for-quite-a-while-to-test-handling-of-larger-inputs")]
    public void Roundtrip_VariousStrings_ReturnOriginal(string plaintext)
    {
        var encrypted = _service.Encrypt(plaintext);
        var decrypted = _service.Decrypt(encrypted);

        Assert.Equal(plaintext, decrypted);
    }
}
