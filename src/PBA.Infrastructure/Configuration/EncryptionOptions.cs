namespace PBA.Infrastructure.Configuration;

public sealed class EncryptionOptions
{
    public const string SectionName = "Encryption";

    public required string Key { get; init; }
}
