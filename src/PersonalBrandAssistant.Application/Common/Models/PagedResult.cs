namespace PersonalBrandAssistant.Application.Common.Models;

public class PagedResult<T>
{
    public PagedResult(IReadOnlyList<T> items, string? cursor, bool hasMore)
    {
        Items = items;
        Cursor = cursor;
        HasMore = hasMore;
    }

    public IReadOnlyList<T> Items { get; }
    public string? Cursor { get; }
    public bool HasMore { get; }

    public static string EncodeCursor(DateTimeOffset createdAt, Guid id) =>
        Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes($"{createdAt.Ticks}_{id}"));

    public static (DateTimeOffset CreatedAt, Guid Id)? DecodeCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return null;

        try
        {
            var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = decoded.Split('_', 2);
            if (parts.Length != 2) return null;

            return (new DateTimeOffset(long.Parse(parts[0]), TimeSpan.Zero), Guid.Parse(parts[1]));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
