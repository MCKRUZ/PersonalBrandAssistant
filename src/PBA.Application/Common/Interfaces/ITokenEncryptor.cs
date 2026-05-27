namespace PBA.Application.Common.Interfaces;

public interface ITokenEncryptor
{
    string Encrypt(string plaintext);
    string Decrypt(string ciphertext);
}
