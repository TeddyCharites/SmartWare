using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartWare.Domain.Constants;
using SmartWare.Domain.Enums;
using SmartWare.Infrastructure.AI;
using SmartWare.Infrastructure.AI.Tools;
using SmartWare.Infrastructure.Data;

namespace SmartWare.UnitTests.AI;

public sealed class ChatToolTests
{
    private static readonly DateOnly Today = new(2026, 10, 8);

    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

    [Fact]
    public void Employee_DoesNotReceiveManagementOrAdminTools()
    {
        var names = ChatToolCatalog.ForRole(RoleNames.Employee).Select(tool => tool.Name).ToArray();

        Assert.Contains(ChatToolCatalog.LookupStock, names);
        Assert.Contains(ChatToolCatalog.SearchKnowledge, names);
        Assert.DoesNotContain(ChatToolCatalog.BusinessReport, names);
        Assert.DoesNotContain(ChatToolCatalog.SuggestReplenishment, names);
        Assert.DoesNotContain(ChatToolCatalog.LookupOrders, names);
    }

    [Fact]
    public void Manager_ReceivesReportsButNotOrders()
    {
        Assert.True(ChatToolCatalog.IsAllowed(ChatToolCatalog.BusinessReport, RoleNames.Manager));
        Assert.True(ChatToolCatalog.IsAllowed(ChatToolCatalog.SuggestReplenishment, RoleNames.Manager));
        Assert.False(ChatToolCatalog.IsAllowed(ChatToolCatalog.LookupOrders, RoleNames.Manager));
    }

    [Fact]
    public void Admin_ReceivesEveryTool()
    {
        Assert.Equal(ChatToolCatalog.All.Count, ChatToolCatalog.ForRole(RoleNames.Admin).Count);
    }

    [Fact]
    public void ToolNames_AreUniqueAndValidForGemini()
    {
        var names = ChatToolCatalog.All.Select(tool => tool.Name).ToArray();

        Assert.Equal(names.Length, names.Distinct().Count());
        Assert.All(names, name => Assert.Matches("^[a-z_]{1,64}$", name));
    }

    [Fact]
    public async Task ExecuteAsync_RejectsToolOutsideRoleBeforeQueryingDatabase()
    {
        // The context points at an unreachable server: any query would throw.
        var dbContext = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=invalid-host;Database=none;Connect Timeout=1")
            .Options);
        var tools = new WarehouseChatTools(dbContext, null!, null!, Options.Create(new GeminiOptions()));

        var result = await tools.ExecuteAsync(
            ChatToolCatalog.BusinessReport,
            Json(new { }),
            RoleNames.Employee);

        Assert.StartsWith("TỪ CHỐI", result.Content);
        Assert.Empty(result.Sources);
    }

    [Fact]
    public void GetDateRange_DefaultsToLastThirtyDays()
    {
        var (from, to) = ChatToolArguments.GetDateRange(Json(new { }), Today);

        Assert.Equal(new DateOnly(2026, 9, 9), from);
        Assert.Equal(Today, to);
    }

    [Fact]
    public void GetDateRange_ParsesExplicitMonth()
    {
        var (from, to) = ChatToolArguments.GetDateRange(
            Json(new { from_date = "2026-08-01", to_date = "2026-08-31" }),
            Today);

        Assert.Equal(new DateOnly(2026, 8, 1), from);
        Assert.Equal(new DateOnly(2026, 8, 31), to);
    }

    [Fact]
    public void GetDateRange_SwapsReversedCapsFutureAndLimitsSpan()
    {
        var reversed = ChatToolArguments.GetDateRange(
            Json(new { from_date = "2026-09-30", to_date = "2026-09-01" }), Today);
        var future = ChatToolArguments.GetDateRange(
            Json(new { from_date = "2026-10-01", to_date = "2027-01-01" }), Today);
        var tooLong = ChatToolArguments.GetDateRange(
            Json(new { from_date = "2020-01-01", to_date = "2026-10-08" }), Today);

        Assert.Equal((new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)), reversed);
        Assert.Equal((new DateOnly(2026, 10, 1), Today), future);
        Assert.Equal(ChatToolArguments.MaxRangeDays, tooLong.To.DayNumber - tooLong.From.DayNumber + 1);
    }

    [Fact]
    public void GetDateRange_IgnoresInvalidDates()
    {
        var (from, to) = ChatToolArguments.GetDateRange(
            Json(new { from_date = "hôm qua", to_date = 42 }), Today);

        Assert.Equal(Today, to);
        Assert.Equal(Today.AddDays(-29), from);
    }

    [Fact]
    public void GetInt_ClampsAndDefaults()
    {
        Assert.Equal(30, ChatToolArguments.GetInt(Json(new { limit = 500 }), "limit", 20, 1, 30));
        Assert.Equal(1, ChatToolArguments.GetInt(Json(new { limit = -3 }), "limit", 20, 1, 30));
        Assert.Equal(20, ChatToolArguments.GetInt(Json(new { limit = "nhiều" }), "limit", 20, 1, 30));
    }

    [Fact]
    public void GetString_TrimsAndBoundsLength()
    {
        var value = ChatToolArguments.GetString(Json(new { keyword = "  " + new string('a', 300) }), "keyword");

        Assert.Equal(200, value!.Length);
        Assert.Null(ChatToolArguments.GetString(Json(new { keyword = "   " }), "keyword"));
    }

    [Theory]
    [InlineData("open", new[] { ReceiptStatus.Pending, ReceiptStatus.Approved })]
    [InlineData("pending", new[] { ReceiptStatus.Pending })]
    [InlineData("unknown-value", new[] { ReceiptStatus.Pending, ReceiptStatus.Approved })]
    public void ParseReceiptStatuses_MapsFilter(string status, ReceiptStatus[] expected)
    {
        Assert.Equal(expected, WarehouseChatTools.ParseReceiptStatuses(status));
    }
}
