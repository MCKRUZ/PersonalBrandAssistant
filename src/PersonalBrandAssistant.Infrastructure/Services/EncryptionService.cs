using System.Text;
using Microsoft.AspNetCore.DataProtection;
using PersonalBrandAssistant.Application.Common.Interfaces;

namespace PersonalBrandAssistant.Infrastructure.Services;

public class EncryptionService : IEncryptionService
{
    private readonly IDataProtector _protector;

    public EncryptionService(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("PersonalBrandAssistant.Secrets");
    }

    public byte[] Encrypt(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        return _protector.Protect(Encoding.UTF8.GetBytes(plaintext));
    }

    public string Decrypt(byte[] ciphertext)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);
        return Encoding.UTF8.GetString(_protector.Unprotect(ciphertext));
    }
}
