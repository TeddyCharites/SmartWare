using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartWare.Application.AI.Rag;
using SmartWare.Domain.Constants;
using SmartWare.Domain.Entities;
using SmartWare.Infrastructure.Data;

namespace SmartWare.Infrastructure.AI;

internal sealed class KnowledgeService(
    ApplicationDbContext dbContext,
    IGeminiEmbeddingService embeddingService,
    IOptions<GeminiOptions> options,
    ILogger<KnowledgeService> logger) : IKnowledgeService
{
    private const double LexicalFallbackMinimumScore = 0.3;
    private readonly GeminiOptions _options = options.Value;

    public async Task<IReadOnlyList<KnowledgeDocumentSummary>> GetDocumentsAsync(
        CancellationToken cancellationToken = default) =>
        await dbContext.KnowledgeDocuments
            .AsNoTracking()
            .OrderByDescending(document => document.CreatedAt)
            .Select(document => new KnowledgeDocumentSummary(
                document.KnowledgeDocumentId,
                document.Title,
                document.Category,
                document.Version,
                document.AllowedRoles,
                document.IsActive,
                document.Chunks.Count,
                document.Chunks.Count(chunk => chunk.EmbeddingJson != null),
                document.CreatedAt))
            .ToListAsync(cancellationToken);

    public async Task<KnowledgeDocumentDetails?> GetDocumentAsync(
        int documentId,
        string role,
        CancellationToken cancellationToken = default)
    {
        if (!RoleNames.All.Contains(role))
        {
            return null;
        }

        var document = await dbContext.KnowledgeDocuments
            .AsNoTracking()
            .Include(item => item.Chunks)
            .SingleOrDefaultAsync(
                item => item.KnowledgeDocumentId == documentId &&
                        ("," + item.AllowedRoles + ",").Contains("," + role + ","),
                cancellationToken);
        if (document is null)
        {
            return null;
        }

        return new KnowledgeDocumentDetails(
            document.KnowledgeDocumentId,
            document.Title,
            document.Category,
            document.Version,
            document.SourceFile,
            document.AllowedRoles,
            document.IsActive,
            document.CreatedAt,
            document.UpdatedAt,
            document.Chunks
                .OrderBy(chunk => chunk.ChunkIndex)
                .Select(chunk => new KnowledgeChunkDetails(
                    chunk.KnowledgeChunkId,
                    chunk.ChunkIndex,
                    chunk.Content,
                    chunk.EmbeddingModel,
                    !string.IsNullOrWhiteSpace(chunk.EmbeddingJson),
                    chunk.EmbeddedAt))
                .ToArray());
    }

    public async Task<KnowledgeOperationResult> CreateAsync(
        CreateKnowledgeDocumentCommand command,
        CancellationToken cancellationToken = default)
    {
        var content = command.Content.Trim();
        if (content.Length < 20)
        {
            return KnowledgeOperationResult.Failure("Nội dung tài liệu phải có ít nhất 20 ký tự.");
        }

        var roles = command.AllowedRoles
            .Where(RoleNames.All.Contains)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (roles.Length == 0)
        {
            return KnowledgeOperationResult.Failure("Tài liệu phải được cấp cho ít nhất một role.");
        }

        var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
        if (await dbContext.KnowledgeDocuments.AnyAsync(
                document => document.Checksum == checksum,
                cancellationToken))
        {
            return KnowledgeOperationResult.Failure("Tài liệu có nội dung trùng với tài liệu đã tồn tại.");
        }

        var chunks = SplitIntoChunks(content);
        var document = new KnowledgeDocument
        {
            Title = command.Title.Trim(),
            Category = command.Category.Trim(),
            Version = command.Version.Trim(),
            SourceFile = string.IsNullOrWhiteSpace(command.SourceFile)
                ? "Nhập trực tiếp"
                : command.SourceFile.Trim(),
            Checksum = checksum,
            AllowedRoles = string.Join(',', roles),
            IsActive = true,
            UploadedById = command.UploadedById,
            CreatedAt = DateTimeOffset.UtcNow
        };
        for (var index = 0; index < chunks.Count; index++)
        {
            document.Chunks.Add(new KnowledgeChunk
            {
                ChunkIndex = index,
                Content = chunks[index],
                Metadata = JsonSerializer.Serialize(new
                {
                    document.Title,
                    document.Category,
                    command.Version
                })
            });
        }

        dbContext.KnowledgeDocuments.Add(document);
        await dbContext.SaveChangesAsync(cancellationToken);

        var indexed = await IndexChunksAsync(document.Chunks.ToArray(), document.Title, cancellationToken);
        return indexed
            ? KnowledgeOperationResult.Success(
                $"Đã thêm tài liệu và lập chỉ mục {chunks.Count} đoạn kiến thức.")
            : KnowledgeOperationResult.Success(
                $"Đã thêm tài liệu gồm {chunks.Count} đoạn. Embedding chưa tạo được; hệ thống sẽ dùng tìm kiếm từ khóa cho đến khi lập chỉ mục lại.");
    }

    public async Task<KnowledgeOperationResult> SetActiveAsync(
        int documentId,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var document = await dbContext.KnowledgeDocuments.SingleOrDefaultAsync(
            item => item.KnowledgeDocumentId == documentId,
            cancellationToken);
        if (document is null)
        {
            return KnowledgeOperationResult.Failure("Không tìm thấy tài liệu.");
        }

        document.IsActive = isActive;
        document.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return KnowledgeOperationResult.Success(
            isActive ? "Đã kích hoạt tài liệu." : "Đã tạm ngừng tài liệu.");
    }

    public async Task<KnowledgeOperationResult> ReindexAsync(
        int? documentId,
        CancellationToken cancellationToken = default)
    {
        if (!embeddingService.IsConfigured)
        {
            return KnowledgeOperationResult.Failure("Chưa cấu hình Gemini API Key để tạo embedding.");
        }

        var query = dbContext.KnowledgeChunks
            .Include(chunk => chunk.KnowledgeDocument)
            .AsQueryable();
        if (documentId.HasValue)
        {
            query = query.Where(chunk => chunk.KnowledgeDocumentId == documentId.Value);
        }

        var chunks = await query
            .OrderBy(chunk => chunk.KnowledgeDocumentId)
            .ThenBy(chunk => chunk.ChunkIndex)
            .ToListAsync(cancellationToken);
        if (chunks.Count == 0)
        {
            return KnowledgeOperationResult.Failure("Không có đoạn kiến thức để lập chỉ mục.");
        }

        var allSucceeded = true;
        foreach (var group in chunks.Chunk(20))
        {
            var title = group[0].KnowledgeDocument.Title;
            allSucceeded &= await IndexChunksAsync(group, title, cancellationToken);
        }

        return allSucceeded
            ? KnowledgeOperationResult.Success($"Đã lập chỉ mục {chunks.Count} đoạn kiến thức.")
            : KnowledgeOperationResult.Failure("Một số embedding chưa tạo được. Vui lòng thử lại.");
    }

    public async Task<IReadOnlyList<KnowledgeSearchHit>> SearchAsync(
        string query,
        string role,
        int take = 5,
        CancellationToken cancellationToken = default)
    {
        if (!RoleNames.All.Contains(role) || string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var candidatesTask = dbContext.KnowledgeChunks
            .AsNoTracking()
            .Where(chunk =>
                chunk.KnowledgeDocument.IsActive &&
                ("," + chunk.KnowledgeDocument.AllowedRoles + ",")
                    .Contains("," + role + ","))
            .OrderBy(chunk => chunk.KnowledgeDocumentId)
            .ThenBy(chunk => chunk.ChunkIndex)
            .Take(2000)
            .Select(chunk => new
            {
                chunk.KnowledgeChunkId,
                chunk.KnowledgeDocumentId,
                DocumentTitle = chunk.KnowledgeDocument.Title,
                chunk.KnowledgeDocument.Category,
                chunk.Content,
                chunk.EmbeddingModel,
                chunk.EmbeddingJson
            })
            .ToListAsync(cancellationToken);
        var queryVectorTask = embeddingService.EmbedQueryAsync(query, cancellationToken);
        await Task.WhenAll(candidatesTask, queryVectorTask);
        var candidates = await candidatesTask;
        if (candidates.Count == 0)
        {
            return [];
        }

        var queryVector = await queryVectorTask;
        var queryTerms = SearchTerms(query);
        var minimumScore = Math.Clamp(_options.RagMinimumScore, 0, 1);
        var results = new List<KnowledgeSearchHit>();

        foreach (var candidate in candidates)
        {
            var lexicalScore = LexicalScore(queryTerms, candidate.Content, candidate.DocumentTitle);
            double? vectorScore = null;
            if (queryVector is not null &&
                string.Equals(candidate.EmbeddingModel, embeddingService.Model, StringComparison.Ordinal) &&
                TryDeserializeVector(candidate.EmbeddingJson, out var vector) &&
                vector.Length == queryVector.Length)
            {
                vectorScore = CosineSimilarity(queryVector, vector);
            }

            var score = vectorScore.HasValue
                ? Math.Min(1, vectorScore.Value + Math.Min(0.08, lexicalScore * 0.08))
                : lexicalScore;
            // With an embedding the combined score must clear the configured threshold; without
            // one (not indexed yet, or the embedding call failed) fall back to keyword overlap.
            var isRelevant = vectorScore.HasValue
                ? score >= minimumScore
                : lexicalScore >= LexicalFallbackMinimumScore;
            if (!isRelevant)
            {
                continue;
            }

            results.Add(new KnowledgeSearchHit(
                candidate.KnowledgeChunkId,
                candidate.KnowledgeDocumentId,
                candidate.DocumentTitle,
                candidate.Category,
                candidate.Content,
                score));
        }

        return results
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.DocumentTitle)
            .Take(Math.Clamp(take, 1, 10))
            .ToArray();
    }

    private async Task<bool> IndexChunksAsync(
        IReadOnlyList<KnowledgeChunk> chunks,
        string title,
        CancellationToken cancellationToken)
    {
        if (!embeddingService.IsConfigured || chunks.Count == 0)
        {
            return false;
        }

        var embeddings = await embeddingService.EmbedDocumentsAsync(
            chunks.Select(chunk => (
                chunk.KnowledgeDocument?.Title ?? title,
                chunk.Content)).ToArray(),
            cancellationToken);
        if (embeddings.Count != chunks.Count)
        {
            logger.LogWarning(
                "Embedding count {EmbeddingCount} does not match chunk count {ChunkCount}.",
                embeddings.Count,
                chunks.Count);
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        for (var index = 0; index < chunks.Count; index++)
        {
            chunks[index].EmbeddingModel = embeddingService.Model;
            chunks[index].EmbeddingJson = JsonSerializer.Serialize(embeddings[index]);
            chunks[index].EmbeddedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    internal static IReadOnlyList<string> SplitIntoChunks(string content)
    {
        const int targetLength = 1200;
        const int overlapLength = 160;
        var paragraphs = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var chunks = new List<string>();
        var current = new StringBuilder();

        foreach (var paragraph in paragraphs)
        {
            foreach (var piece in SplitLongText(paragraph, targetLength))
            {
                if (current.Length > 0 && current.Length + piece.Length + 2 > targetLength)
                {
                    var completed = current.ToString().Trim();
                    chunks.Add(completed);
                    current.Clear();
                    var overlap = completed.Length <= overlapLength
                        ? completed
                        : completed[^overlapLength..];
                    current.Append(overlap.TrimStart()).AppendLine().AppendLine();
                }

                current.Append(piece.Trim()).AppendLine().AppendLine();
            }
        }

        if (current.Length > 0)
        {
            chunks.Add(current.ToString().Trim());
        }

        return chunks.Count == 0 ? [content.Trim()] : chunks;
    }

    private static IEnumerable<string> SplitLongText(string text, int maxLength)
    {
        var remaining = text.Trim();
        while (remaining.Length > maxLength)
        {
            var split = remaining.LastIndexOf(' ', maxLength);
            if (split < maxLength / 2)
            {
                split = maxLength;
            }

            yield return remaining[..split];
            remaining = remaining[split..].TrimStart();
        }

        if (remaining.Length > 0)
        {
            yield return remaining;
        }
    }

    private static HashSet<string> SearchTerms(string value) => Normalize(value)
        .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(term => term.Length >= 3)
        .ToHashSet(StringComparer.Ordinal);

    private static double LexicalScore(
        IReadOnlySet<string> queryTerms,
        string content,
        string title)
    {
        if (queryTerms.Count == 0)
        {
            return 0;
        }

        var normalizedContent = Normalize(content);
        var normalizedTitle = Normalize(title);
        var matched = queryTerms.Count(term =>
            normalizedContent.Contains(term, StringComparison.Ordinal));
        var titleMatched = queryTerms.Count(term =>
            normalizedTitle.Contains(term, StringComparison.Ordinal));
        return Math.Min(1, (double)matched / queryTerms.Count + titleMatched * 0.12);
    }

    private static double CosineSimilarity(float[] left, float[] right)
    {
        double dot = 0;
        double leftMagnitude = 0;
        double rightMagnitude = 0;
        for (var index = 0; index < left.Length; index++)
        {
            dot += left[index] * right[index];
            leftMagnitude += left[index] * left[index];
            rightMagnitude += right[index] * right[index];
        }

        var denominator = Math.Sqrt(leftMagnitude) * Math.Sqrt(rightMagnitude);
        return denominator <= double.Epsilon ? 0 : dot / denominator;
    }

    private static bool TryDeserializeVector(string? json, out float[] vector)
    {
        vector = [];
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            vector = JsonSerializer.Deserialize<float[]>(json) ?? [];
            return vector.Length > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Normalize(string value)
    {
        var decomposed = value.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(character) ? character : ' ');
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
