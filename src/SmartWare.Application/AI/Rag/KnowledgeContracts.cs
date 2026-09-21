namespace SmartWare.Application.AI.Rag;

public sealed record KnowledgeSearchHit(
    long ChunkId,
    int DocumentId,
    string DocumentTitle,
    string Category,
    string Content,
    double Score);

public sealed record KnowledgeDocumentSummary(
    int Id,
    string Title,
    string Category,
    string Version,
    string AllowedRoles,
    bool IsActive,
    int ChunkCount,
    int EmbeddedChunkCount,
    DateTimeOffset CreatedAt);

public sealed record KnowledgeChunkDetails(
    long Id,
    int Index,
    string Content,
    string? EmbeddingModel,
    bool IsEmbedded,
    DateTimeOffset? EmbeddedAt);

public sealed record KnowledgeDocumentDetails(
    int Id,
    string Title,
    string Category,
    string Version,
    string SourceFile,
    string AllowedRoles,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<KnowledgeChunkDetails> Chunks);

public sealed record CreateKnowledgeDocumentCommand(
    string Title,
    string Category,
    string Version,
    string SourceFile,
    string Content,
    IReadOnlyList<string> AllowedRoles,
    string UploadedById);

public sealed record KnowledgeOperationResult(bool Succeeded, string Message)
{
    public static KnowledgeOperationResult Success(string message) => new(true, message);
    public static KnowledgeOperationResult Failure(string message) => new(false, message);
}

public interface IKnowledgeService
{
    Task<IReadOnlyList<KnowledgeDocumentSummary>> GetDocumentsAsync(
        CancellationToken cancellationToken = default);

    Task<KnowledgeDocumentDetails?> GetDocumentAsync(
        int documentId,
        string role,
        CancellationToken cancellationToken = default);

    Task<KnowledgeOperationResult> CreateAsync(
        CreateKnowledgeDocumentCommand command,
        CancellationToken cancellationToken = default);

    Task<KnowledgeOperationResult> SetActiveAsync(
        int documentId,
        bool isActive,
        CancellationToken cancellationToken = default);

    Task<KnowledgeOperationResult> ReindexAsync(
        int? documentId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KnowledgeSearchHit>> SearchAsync(
        string query,
        string role,
        int take = 5,
        CancellationToken cancellationToken = default);
}

public interface IGeminiEmbeddingService
{
    bool IsConfigured { get; }
    string Model { get; }

    Task<float[]?> EmbedQueryAsync(
        string text,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(
        IReadOnlyList<(string Title, string Content)> documents,
        CancellationToken cancellationToken = default);
}
