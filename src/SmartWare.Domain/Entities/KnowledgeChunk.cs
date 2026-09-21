namespace SmartWare.Domain.Entities;

public sealed class KnowledgeChunk
{
    public long KnowledgeChunkId { get; set; }
    public int KnowledgeDocumentId { get; set; }
    public int ChunkIndex { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? Metadata { get; set; }
    public string? EmbeddingModel { get; set; }
    public string? EmbeddingReference { get; set; }
    public string? EmbeddingJson { get; set; }
    public DateTimeOffset? EmbeddedAt { get; set; }

    public KnowledgeDocument KnowledgeDocument { get; set; } = null!;
}
