using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartWare.Application.AI.Rag;

namespace SmartWare.Infrastructure.AI;

internal sealed class GeminiEmbeddingService(
    HttpClient httpClient,
    IOptions<GeminiOptions> options,
    ILogger<GeminiEmbeddingService> logger) : IGeminiEmbeddingService
{
    private readonly GeminiOptions _options = options.Value;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);
    public string Model => string.IsNullOrWhiteSpace(_options.EmbeddingModel)
        ? "gemini-embedding-2"
        : _options.EmbeddingModel.Trim();

    public async Task<float[]?> EmbedQueryAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var payload = new
        {
            content = new
            {
                parts = new[]
                {
                    new { text = $"task: question answering | query: {Limit(text, 6000)}" }
                }
            },
            output_dimensionality = Math.Clamp(_options.EmbeddingDimensions, 128, 3072)
        };

        using var request = CreateRequest($"models/{Uri.EscapeDataString(Model)}:embedContent", payload);
        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Gemini embedding query returned HTTP {StatusCode} for model {Model}.",
                    (int)response.StatusCode,
                    Model);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return ReadVector(document.RootElement.GetProperty("embedding"));
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException)
        {
            logger.LogWarning(exception, "Cannot create Gemini query embedding.");
            return null;
        }
    }

    public async Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(
        IReadOnlyList<(string Title, string Content)> documents,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured || documents.Count == 0)
        {
            return [];
        }

        var modelResource = $"models/{Model}";
        var requests = documents.Select(document => new
        {
            model = modelResource,
            content = new
            {
                parts = new[]
                {
                    new
                    {
                        text = $"title: {Limit(document.Title, 300)} | text: {Limit(document.Content, 6000)}"
                    }
                }
            },
            output_dimensionality = Math.Clamp(_options.EmbeddingDimensions, 128, 3072)
        }).ToArray();
        var payload = new { requests };

        using var request = CreateRequest(
            $"models/{Uri.EscapeDataString(Model)}:batchEmbedContents",
            payload);
        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Gemini document embedding returned HTTP {StatusCode} for model {Model}.",
                    (int)response.StatusCode,
                    Model);
                return [];
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("embeddings", out var embeddings))
            {
                return [];
            }

            return embeddings.EnumerateArray().Select(ReadVector).ToArray();
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException)
        {
            logger.LogWarning(exception, "Cannot create Gemini document embeddings.");
            return [];
        }
    }

    private HttpRequestMessage CreateRequest(string endpoint, object payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Add("x-goog-api-key", _options.ApiKey!.Trim());
        request.Content = JsonContent.Create(payload);
        return request;
    }

    private static float[] ReadVector(JsonElement embedding) => embedding
        .GetProperty("values")
        .EnumerateArray()
        .Select(value => value.GetSingle())
        .ToArray();

    private static string Limit(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
