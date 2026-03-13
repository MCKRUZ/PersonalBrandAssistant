namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface IEncryptionService
{
    byte[] Encrypt(string plaintext);
    string Decrypt(byte[] ciphertext);
}
