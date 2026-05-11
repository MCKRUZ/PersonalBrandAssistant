using PBA.Application.Common.Models;
using Xunit;

namespace PBA.Application.Tests.Common;

public class PagedResultTests
{
    [Fact]
    public void TotalPages_ExactDivision_CalculatesCorrectly()
    {
        var result = new PagedResult<string>
        {
            Items = Enumerable.Range(1, 50).Select(i => i.ToString()).ToList(),
            TotalCount = 100,
            Page = 1,
            PageSize = 50
        };

        Assert.Equal(2, result.TotalPages);
    }

    [Fact]
    public void TotalPages_PartialPage_RoundsUp()
    {
        var result = new PagedResult<string>
        {
            Items = Enumerable.Range(1, 50).Select(i => i.ToString()).ToList(),
            TotalCount = 101,
            Page = 1,
            PageSize = 50
        };

        Assert.Equal(3, result.TotalPages);
    }

    [Fact]
    public void TotalPages_ZeroTotalCount_ReturnsZero()
    {
        var result = new PagedResult<string>
        {
            Items = [],
            TotalCount = 0,
            Page = 1,
            PageSize = 50
        };

        Assert.Equal(0, result.TotalPages);
    }

    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var items = new List<int> { 1, 2, 3 };

        var result = new PagedResult<int>
        {
            Items = items,
            TotalCount = 25,
            Page = 2,
            PageSize = 10
        };

        Assert.Equal(items, result.Items);
        Assert.Equal(25, result.TotalCount);
        Assert.Equal(2, result.Page);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(3, result.TotalPages);
    }
}
