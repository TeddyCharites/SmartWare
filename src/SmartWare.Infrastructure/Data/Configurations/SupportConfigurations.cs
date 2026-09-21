using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartWare.Domain.Entities;
using SmartWare.Infrastructure.Identity;

namespace SmartWare.Infrastructure.Data.Configurations;

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");
        builder.HasKey(x => x.AuditLogId);
        builder.Property(x => x.UserId).HasMaxLength(450);
        builder.Property(x => x.Action).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Module).HasMaxLength(100).IsRequired();
        builder.Property(x => x.EntityType).HasMaxLength(150).IsRequired();
        builder.Property(x => x.EntityId).HasMaxLength(100);
        builder.Property(x => x.OldValues).HasColumnType("nvarchar(max)");
        builder.Property(x => x.NewValues).HasColumnType("nvarchar(max)");
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSDATETIMEOFFSET()");
        builder.HasIndex(x => new { x.Module, x.CreatedAt });
        builder.HasIndex(x => new { x.EntityType, x.EntityId });

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class KnowledgeDocumentConfiguration : IEntityTypeConfiguration<KnowledgeDocument>
{
    public void Configure(EntityTypeBuilder<KnowledgeDocument> builder)
    {
        builder.ToTable("KnowledgeDocuments");
        builder.HasKey(x => x.KnowledgeDocumentId);
        builder.Property(x => x.Title).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Category).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Version).HasMaxLength(50).IsRequired();
        builder.Property(x => x.SourceFile).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Checksum).HasMaxLength(128).IsRequired();
        builder.Property(x => x.AllowedRoles).HasMaxLength(100).IsRequired();
        builder.Property(x => x.UploadedById).HasMaxLength(450).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSDATETIMEOFFSET()");
        builder.HasIndex(x => x.Checksum).IsUnique();
        builder.HasIndex(x => new { x.IsActive, x.Category });

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UploadedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class KnowledgeChunkConfiguration : IEntityTypeConfiguration<KnowledgeChunk>
{
    public void Configure(EntityTypeBuilder<KnowledgeChunk> builder)
    {
        builder.ToTable("KnowledgeChunks");
        builder.HasKey(x => x.KnowledgeChunkId);
        builder.Property(x => x.Content).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.Metadata).HasColumnType("nvarchar(max)");
        builder.Property(x => x.EmbeddingModel).HasMaxLength(100);
        builder.Property(x => x.EmbeddingReference).HasMaxLength(500);
        builder.Property(x => x.EmbeddingJson).HasColumnType("nvarchar(max)");
        builder.HasIndex(x => new { x.KnowledgeDocumentId, x.ChunkIndex }).IsUnique();

        builder.HasOne(x => x.KnowledgeDocument)
            .WithMany(x => x.Chunks)
            .HasForeignKey(x => x.KnowledgeDocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ChatSessionConfiguration : IEntityTypeConfiguration<ChatSession>
{
    public void Configure(EntityTypeBuilder<ChatSession> builder)
    {
        builder.ToTable("ChatSessions");
        builder.HasKey(x => x.ChatSessionId);
        builder.Property(x => x.UserId).HasMaxLength(450).IsRequired();
        builder.Property(x => x.AccessRole).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSDATETIMEOFFSET()");
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("SYSDATETIMEOFFSET()");
        builder.HasIndex(x => new { x.UserId, x.AccessRole, x.UpdatedAt });

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        builder.ToTable("ChatMessages");
        builder.HasKey(x => x.ChatMessageId);
        builder.Property(x => x.Role).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Content).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.SourcesJson).HasColumnType("nvarchar(max)");
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSDATETIMEOFFSET()");
        builder.HasIndex(x => new { x.ChatSessionId, x.CreatedAt });

        builder.HasOne(x => x.Session)
            .WithMany(x => x.Messages)
            .HasForeignKey(x => x.ChatSessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
