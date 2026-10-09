using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using SmartWare.Application.AI.Chatbot;
using SmartWare.Domain.Constants;
using SmartWare.Infrastructure.AI;
using SmartWare.Infrastructure.AI.Tools;

namespace SmartWare.UnitTests.AI;

public sealed class ChatDraftTests
{
    private static ChatReceiptDraft Draft(bool canConfirm = true) => new(
        Guid.NewGuid(), ChatDraftTypes.Export, 1, "Kho chính", null, null, null, null,
        [new ChatReceiptDraftLine(7, "SP007", "Ổ cứng SSD", "Chiếc", 3, 1_500_000, 9)],
        4_500_000, [], canConfirm);

    [Fact]
    public void Store_ReturnsDraftOnlyToItsOwner()
    {
        var store = new ChatDraftStore(new MemoryCache(new MemoryCacheOptions()));
        var draft = Draft();
        store.Save("owner", draft);

        Assert.Equal(draft, store.Get("owner", draft.Id));
        Assert.Null(store.Get("intruder", draft.Id));
        Assert.Null(store.Take("intruder", draft.Id));
        Assert.NotNull(store.Get("owner", draft.Id));
    }

    [Fact]
    public void Store_TakeIsSingleUse()
    {
        var store = new ChatDraftStore(new MemoryCache(new MemoryCacheOptions()));
        var draft = Draft();
        store.Save("owner", draft);

        Assert.Equal(draft, store.Take("owner", draft.Id));
        Assert.Null(store.Take("owner", draft.Id));
        Assert.Null(store.Get("owner", draft.Id));
    }

    [Fact]
    public void Store_TakeUnderConcurrencyReturnsDraftOnce()
    {
        var store = new ChatDraftStore(new MemoryCache(new MemoryCacheOptions()));
        var draft = Draft();
        store.Save("owner", draft);

        var taken = Enumerable.Range(0, 32)
            .AsParallel()
            .Select(_ => store.Take("owner", draft.Id))
            .Count(result => result is not null);

        Assert.Equal(1, taken);
    }

    [Theory]
    [InlineData(RoleNames.Admin, true)]
    [InlineData(RoleNames.Employee, true)]
    [InlineData(RoleNames.Manager, false)]
    public void DraftTools_FollowCreateReceiptsPolicy(string role, bool allowed)
    {
        Assert.Equal(allowed, ChatToolCatalog.IsAllowed(ChatToolCatalog.DraftImportReceipt, role));
        Assert.Equal(allowed, ChatToolCatalog.IsAllowed(ChatToolCatalog.DraftExportReceipt, role));
    }

    [Fact]
    public void GetDraftLines_ParsesAndBoundsLines()
    {
        var args = JsonSerializer.SerializeToElement(new
        {
            lines = new object[]
            {
                new { product = "RAM DDR5", quantity = 50, unit_cost = 1_250_000.555 },
                new { product = "SSD", quantity = 2_000_000 },
                new { product = "", quantity = 3 },
                new { product = "Chuột", quantity = -4 }
            }
        });

        var lines = ChatToolArguments.GetDraftLines(args);

        Assert.Equal(4, lines.Count);
        Assert.Equal(new DraftLineRequest("RAM DDR5", 50, 1_250_000.56m), lines[0]);
        Assert.Equal(1_000_000, lines[1].Quantity);
        Assert.Null(lines[1].UnitCost);
        Assert.Equal("", lines[2].Product);
        Assert.Equal(0, lines[3].Quantity);
    }

    [Fact]
    public void GetDraftLines_CapsNumberOfLines()
    {
        var args = JsonSerializer.SerializeToElement(new
        {
            lines = Enumerable.Range(1, 50).Select(i => new { product = $"SP{i:000}", quantity = i }).ToArray()
        });

        Assert.Equal(30, ChatToolArguments.GetDraftLines(args).Count);
    }

    [Fact]
    public void DraftResult_TellsGeminiTheReceiptIsNotCreated()
    {
        var result = WarehouseChatTools.DraftResult(Draft(canConfirm: false));

        Assert.Contains("CHƯA được tạo", result.Content);
        Assert.Contains("CHƯA thể xác nhận", result.Content);
        Assert.NotNull(result.Draft);
    }
}
