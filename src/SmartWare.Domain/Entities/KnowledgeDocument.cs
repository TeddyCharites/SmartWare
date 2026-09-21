namespace SmartWare.Domain.Entities;

public sealed class KnowledgeDocument
{
    public int KnowledgeDocumentId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string SourceFile { get; set; } = string.Empty;
    public string Checksum { get; set; } = string.Empty;
    public string AllowedRoles { get; set; } = "Admin,Manager,Employee";
    public bool IsActive { get; set; } = true;
    public string UploadedById { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    public ICollection<KnowledgeChunk> Chunks { get; set; } = new List<KnowledgeChunk>();
}
