using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartWare.Application.AI.Chatbot;
using SmartWare.Domain.Constants;
using SmartWare.Infrastructure.AI;
using SmartWare.Infrastructure.AI.Tools;

namespace SmartWare.UnitTests.AI;

public sealed class GeminiServiceAgentTests
{
    private const string StockSource = "SQL Server: sản phẩm và tồn kho hiện tại";

    [Fact]
    public async Task AskAsync_ExecutesRequestedToolAndReturnsGroundedAnswer()
    {
        var handler = new QueuedGeminiHandler(
            FunctionCallResponse("lookup_stock", new { keyword = "SP001" }, thoughtSignature: "sig-abc"),
            TextResponse("SP001 còn **50 hộp** khả dụng."));
        var tools = new FakeChatTools();
        var service = CreateService(handler, tools);

        var response = await service.AskAsync(
            new ChatRequest { Message = "Còn bao nhiêu SP001?" },
            new ChatUserContext("user-1", RoleNames.Employee, []));

        Assert.True(response.Success);
        Assert.True(response.IsAiGenerated);
        Assert.Equal("SP001 còn **50 hộp** khả dụng.", response.Answer);
        Assert.Equal([StockSource], response.Sources);

        var call = Assert.Single(tools.Calls);
        Assert.Equal(("lookup_stock", "SP001", RoleNames.Employee), call);

        Assert.Equal(2, handler.RequestBodies.Count);
        // The model turn is replayed verbatim, keeping the thought signature Gemini 3 requires.
        using var secondRequest = JsonDocument.Parse(handler.RequestBodies[1]);
        var contents = secondRequest.RootElement.GetProperty("contents").EnumerateArray().ToArray();
        Assert.Equal(3, contents.Length);
        Assert.Equal("model", contents[1].GetProperty("role").GetString());
        Assert.Equal("sig-abc", contents[1].GetProperty("parts")[0].GetProperty("thoughtSignature").GetString());
        var functionResponse = contents[2].GetProperty("parts")[0].GetProperty("functionResponse");
        Assert.Equal("lookup_stock", functionResponse.GetProperty("name").GetString());
        Assert.StartsWith("TỒN KHO", functionResponse.GetProperty("response").GetProperty("result").GetString());
    }

    [Fact]
    public async Task AskAsync_SendsOnlyToolsAllowedForRole()
    {
        var handler = new QueuedGeminiHandler(TextResponse("Xin chào"));
        var service = CreateService(handler, new FakeChatTools());

        await service.AskAsync(
            new ChatRequest { Message = "Xin chào" },
            new ChatUserContext("user-1", RoleNames.Employee, []));

        var declared = DeclaredToolNames(handler.RequestBodies[0]);
        Assert.Contains(ChatToolCatalog.LookupStock, declared);
        Assert.DoesNotContain(ChatToolCatalog.BusinessReport, declared);
        Assert.DoesNotContain(ChatToolCatalog.LookupOrders, declared);
    }

    [Fact]
    public async Task AskAsync_SendsHistoryAsMultiTurnConversation()
    {
        var handler = new QueuedGeminiHandler(TextResponse("Nhà cung cấp là ABC."));
        var service = CreateService(handler, new FakeChatTools());
        var history = new[]
        {
            new ChatHistoryMessage(1, "user", "Tồn kho SP001?", [], false, DateTimeOffset.UtcNow),
            new ChatHistoryMessage(2, "assistant", "SP001 còn 50 hộp.", [], true, DateTimeOffset.UtcNow)
        };

        await service.AskAsync(
            new ChatRequest { Message = "Nhà cung cấp của nó là ai?" },
            new ChatUserContext("user-1", RoleNames.Employee, history));

        using var body = JsonDocument.Parse(handler.RequestBodies[0]);
        var roles = body.RootElement.GetProperty("contents").EnumerateArray()
            .Select(content => content.GetProperty("role").GetString()!)
            .ToArray();
        Assert.Equal(["user", "model", "user"], roles);
    }

    [Fact]
    public async Task AskAsync_ForcesFinalAnswerAfterToolRoundLimit()
    {
        var handler = new QueuedGeminiHandler(
            FunctionCallResponse("list_low_stock", new { }),
            FunctionCallResponse("list_low_stock", new { }),
            TextResponse("Có 2 sản phẩm sắp hết."));
        var service = CreateService(handler, new FakeChatTools(), maxToolRounds: 2);

        var response = await service.AskAsync(
            new ChatRequest { Message = "Hàng nào sắp hết?" },
            new ChatUserContext("user-1", RoleNames.Manager, []));

        Assert.True(response.Success);
        Assert.Equal(3, handler.RequestBodies.Count);
        Assert.Contains("\"mode\":\"AUTO\"", handler.RequestBodies[0]);
        Assert.Contains("\"mode\":\"NONE\"", handler.RequestBodies[2]);
    }

    [Fact]
    public async Task AskAsync_FallsBackToSqlWhenGeminiFails()
    {
        var handler = new QueuedGeminiHandler(
            new HttpResponseMessage(HttpStatusCode.BadRequest));
        var tools = new FakeChatTools();
        var service = CreateService(handler, tools);

        var response = await service.AskAsync(
            new ChatRequest { Message = "Tồn kho SP001 còn bao nhiêu?" },
            new ChatUserContext("user-1", RoleNames.Employee, []));

        Assert.True(response.Success);
        Assert.False(response.IsAiGenerated);
        Assert.Contains("SP001 — Sữa tươi", response.Answer);
        Assert.Equal(ChatToolCatalog.LookupStock, Assert.Single(tools.Calls).Tool);
    }

    [Fact]
    public async Task AskAsync_OfflineFallbackStillEnforcesRole()
    {
        var handler = new QueuedGeminiHandler(
            new HttpResponseMessage(HttpStatusCode.BadRequest));
        var tools = new FakeChatTools();
        var service = CreateService(handler, tools);

        var response = await service.AskAsync(
            new ChatRequest { Message = "Doanh thu tháng này bao nhiêu?" },
            new ChatUserContext("user-1", RoleNames.Employee, []));

        Assert.Contains("không được phép", response.Answer);
        Assert.Empty(tools.Calls);
    }

    [Fact]
    public async Task AskAsync_WithoutApiKeyUsesOfflineMode()
    {
        var handler = new QueuedGeminiHandler();
        var tools = new FakeChatTools();
        var service = CreateService(handler, tools, apiKey: null);

        var response = await service.AskAsync(
            new ChatRequest { Message = "Tồn kho SP001?" },
            new ChatUserContext("user-1", RoleNames.Employee, []));

        Assert.True(response.Success);
        Assert.False(response.IsAiGenerated);
        Assert.Empty(handler.RequestBodies);
    }

    [Fact]
    public async Task AskAsync_RetriesOnceWhenGeminiSuggestsShortWait()
    {
        var handler = new QueuedGeminiHandler(
            RateLimited("""{"error":{"code":429,"details":[{"retryDelay":"0s"}]}}"""),
            TextResponse("Đã trả lời sau khi thử lại."));
        var service = CreateService(handler, new FakeChatTools());

        var response = await service.AskAsync(
            new ChatRequest { Message = "Xin chào" },
            new ChatUserContext("user-1", RoleNames.Employee, []));

        Assert.True(response.IsAiGenerated);
        Assert.Equal(2, handler.RequestBodies.Count);
    }

    [Fact]
    public async Task AskAsync_RetriesOnceWhenGeminiIsTemporarilyOverloaded()
    {
        var handler = new QueuedGeminiHandler(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            TextResponse("Đã trả lời sau khi Gemini hết quá tải."));
        var service = CreateService(handler, new FakeChatTools());

        var response = await service.AskAsync(
            new ChatRequest { Message = "Xin chào" },
            new ChatUserContext("user-1", RoleNames.Employee, []));

        Assert.True(response.IsAiGenerated);
        Assert.Equal(2, handler.RequestBodies.Count);
    }

    [Fact]
    public async Task AskAsync_DoesNotWaitWhenRetryDelayIsLong()
    {
        var handler = new QueuedGeminiHandler(
            RateLimited("""{"error":{"code":429,"details":[{"retryDelay":"45s"}]}}"""));
        var service = CreateService(handler, new FakeChatTools());

        var response = await service.AskAsync(
            new ChatRequest { Message = "Xin chào" },
            new ChatUserContext("user-1", RoleNames.Employee, []));

        // Falls back to SQL immediately instead of blocking the user for 45 seconds.
        Assert.False(response.IsAiGenerated);
        Assert.Single(handler.RequestBodies);
    }

    [Fact]
    public async Task AskAsync_SwitchesToFallbackModelWhenQuotaIsExhausted()
    {
        var handler = new QueuedGeminiHandler(
            RateLimited("""{"error":{"code":429,"details":[{"retryDelay":"53125s"}]}}"""),
            TextResponse("Trả lời bằng model dự phòng."));
        var service = CreateService(handler, new FakeChatTools(), fallbackModels: ["fallback-model"]);

        var response = await service.AskAsync(
            new ChatRequest { Message = "Xin chào" },
            new ChatUserContext("user-1", RoleNames.Employee, []));

        Assert.True(response.IsAiGenerated);
        Assert.Equal("Trả lời bằng model dự phòng.", response.Answer);
        Assert.Equal(
            ["/v1beta/models/primary-model:generateContent", "/v1beta/models/fallback-model:generateContent"],
            handler.RequestPaths);
    }

    [Fact]
    public async Task AskAsync_ReturnsDraftStoredForCurrentUserOnly()
    {
        var handler = new QueuedGeminiHandler(
            FunctionCallResponse("draft_import_receipt", new { lines = new[] { new { product = "SP001", quantity = 5 } } }),
            TextResponse("Đã soạn bản nháp, vui lòng kiểm tra và bấm Xác nhận."));
        var store = new ChatDraftStore(new MemoryCache(new MemoryCacheOptions()));
        var service = CreateService(handler, new FakeChatTools(), draftStore: store);

        var response = await service.AskAsync(
            new ChatRequest { Message = "Tạo phiếu nhập 5 SP001" },
            new ChatUserContext("user-1", RoleNames.Employee, []));

        Assert.NotNull(response.Draft);
        Assert.NotEqual(Guid.Empty, response.Draft.Id);
        Assert.Equal(response.Draft, store.Get("user-1", response.Draft.Id));
        Assert.Null(store.Get("someone-else", response.Draft.Id));
    }

    [Fact]
    public void ParseReply_IgnoresThoughtSummaryParts()
    {
        using var document = JsonDocument.Parse("""
            {"candidates":[{"content":{"role":"model","parts":[
              {"text":"suy nghĩ nội bộ","thought":true},
              {"text":"Câu trả lời"}]},"finishReason":"STOP"}]}
            """);

        var reply = GeminiService.ParseReply(document.RootElement);

        Assert.NotNull(reply);
        Assert.Equal("Câu trả lời", reply.Text);
        Assert.Empty(reply.FunctionCalls);
    }

    private static GeminiService CreateService(
        HttpMessageHandler handler,
        IWarehouseChatTools tools,
        string? apiKey = "test-key",
        int maxToolRounds = 4,
        string[]? fallbackModels = null,
        IChatDraftStore? draftStore = null) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://gemini.test/v1beta/") },
            tools,
            draftStore ?? new ChatDraftStore(new MemoryCache(new MemoryCacheOptions())),
            Options.Create(new GeminiOptions
            {
                ApiKey = apiKey,
                Model = "primary-model",
                MaxToolRounds = maxToolRounds,
                FallbackModels = fallbackModels ?? []
            }),
            NullLogger<GeminiService>.Instance);

    private static string[] DeclaredToolNames(string requestBody)
    {
        using var body = JsonDocument.Parse(requestBody);
        return body.RootElement.GetProperty("tools")[0].GetProperty("functionDeclarations")
            .EnumerateArray()
            .Select(tool => tool.GetProperty("name").GetString()!)
            .ToArray();
    }

    private static HttpResponseMessage TextResponse(string text) => Json(new
    {
        candidates = new[]
        {
            new
            {
                content = new { role = "model", parts = new[] { new { text } } },
                finishReason = "STOP"
            }
        }
    });

    private static HttpResponseMessage FunctionCallResponse(
        string name,
        object args,
        string thoughtSignature = "sig") => Json(new
    {
        candidates = new[]
        {
            new
            {
                content = new
                {
                    role = "model",
                    parts = new[] { new { functionCall = new { name, args }, thoughtSignature } }
                },
                finishReason = "STOP"
            }
        }
    });

    private static HttpResponseMessage RateLimited(string body) => new(HttpStatusCode.TooManyRequests)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
    };

    private sealed class QueuedGeminiHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        public List<string> RequestBodies { get; } = [];

        public List<string> RequestPaths { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            RequestPaths.Add(request.RequestUri!.AbsolutePath);
            return _responses.Count > 0
                ? _responses.Dequeue()
                : new HttpResponseMessage(HttpStatusCode.InternalServerError);
        }
    }

    private sealed class FakeChatTools : IWarehouseChatTools
    {
        public List<(string Tool, string? Keyword, string Role)> Calls { get; } = [];

        public Task<ChatToolResult> ExecuteAsync(
            string toolName,
            JsonElement arguments,
            string role,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((toolName, ChatToolArguments.GetString(arguments, "keyword"), role));
            if (toolName == ChatToolCatalog.DraftImportReceipt)
            {
                return Task.FromResult(WarehouseChatTools.DraftResult(new ChatReceiptDraft(
                    Guid.Empty, ChatDraftTypes.Import, 1, "Kho chính", 2, "NCC Demo", null, null,
                    [new ChatReceiptDraftLine(10, "SP001", "Sữa tươi", "Hộp", 5, 20_000, null)],
                    100_000, [], true)));
            }

            return Task.FromResult(new ChatToolResult(
                "TỒN KHO HIỆN TẠI:\n- SP001 | Sữa tươi | khả dụng: 50",
                [StockSource],
                FallbackAnswer: "- **SP001 — Sữa tươi**: khả dụng **50**."));
        }

        public Task<bool> HasMatchingProductAsync(string text, CancellationToken cancellationToken = default) =>
            Task.FromResult(text.Contains("SP001", StringComparison.OrdinalIgnoreCase));
    }
}
