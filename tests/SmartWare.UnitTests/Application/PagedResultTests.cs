using SmartWare.Application.Common;

namespace SmartWare.UnitTests.Application;

public sealed class PagedResultTests
{
    [Theory]
    [InlineData(0, 10, 1)]
    [InlineData(10, 10, 1)]
    [InlineData(11, 10, 2)]
    [InlineData(25, 10, 3)]
    public void TotalPages_RoundsUpAndNeverReturnsZero(
        int totalCount,
        int pageSize,
        int expected)
    {
        var result = new PagedResult<int>([], 1, pageSize, totalCount);

        Assert.Equal(expected, result.TotalPages);
    }

    [Fact]
    public void NavigationFlags_ReflectCurrentPage()
    {
        var result = new PagedResult<int>([], 2, 10, 25);

        Assert.True(result.HasPreviousPage);
        Assert.True(result.HasNextPage);
    }
}
