using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Application.Tests.Common;

public class PagedResultTests
{
    [Fact]
    public void EncodeCursor_DecodeCursor_Roundtrip()
    {
        var createdAt = new DateTimeOffset(2026, 3, 13, 10, 0, 0, TimeSpan.Zero);
        var id = Guid.NewGuid();

        var cursor = PagedResult<object>.EncodeCursor(createdAt, id);
        var decoded = PagedResult<object>.DecodeCursor(cursor);

        Assert.NotNull(decoded);
        Assert.Equal(createdAt, decoded.Value.CreatedAt);
        Assert.Equal(id, decoded.Value.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DecodeCursor_NullOrWhitespace_ReturnsNull(string? cursor)
    {
        var result = PagedResult<object>.DecodeCursor(cursor);
        Assert.Null(result);
    }

    [Fact]
    public void Constructor_SetsProperties()
    {
        var items = new List<string> { "a", "b" }.AsReadOnly();
        var paged = new PagedResult<string>(items, "cursor123", true);

        Assert.Equal(2, paged.Items.Count);
        Assert.Equal("cursor123", paged.Cursor);
        Assert.True(paged.HasMore);
    }
}
