using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;

public class JsonValueConverter<T> : ValueConverter<T, string> where T : class, new()
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public JsonValueConverter() : base(
        v => JsonSerializer.Serialize(v, Options),
        v => JsonSerializer.Deserialize<T>(v, Options) ?? new T())
    {
    }
}
