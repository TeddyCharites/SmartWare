using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartWare.Domain.Entities;
using SmartWare.Infrastructure.Identity;

namespace SmartWare.Infrastructure.Data.Configurations;

internal sealed class ImportReceiptConfiguration : IEntityTypeConfiguration<ImportReceipt>
{
    public void Configure(EntityTypeBuilder<ImportReceipt> builder)
    {
        builder.ToTable("ImportReceipts");
        builder.HasKey(x => x.ImportReceiptId);
        ConfigureReceipt(builder);

        builder.HasOne(x => x.Supplier)
            .WithMany(x => x.ImportReceipts)
            .HasForeignKey(x => x.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Warehouse)
            .WithMany(x => x.ImportReceipts)
            .HasForeignKey(x => x.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        ConfigureUserRelationships(builder);
    }

    private static void ConfigureReceipt(EntityTypeBuilder<ImportReceipt> builder)
    {
        builder.Property(x => x.ReceiptNumber).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.CreatedById).HasMaxLength(450).IsRequired();
        builder.Property(x => x.ApprovedById).HasMaxLength(450);
        builder.Property(x => x.CompletedById).HasMaxLength(450);
        builder.Property(x => x.RejectionReason).HasMaxLength(1000);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSDATETIMEOFFSET()");
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => x.ReceiptNumber).IsUnique();
        builder.HasIndex(x => new { x.Status, x.CreatedAt });
    }

    private static void ConfigureUserRelationships(EntityTypeBuilder<ImportReceipt> builder)
    {
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ApprovedById)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CompletedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ImportReceiptDetailConfiguration : IEntityTypeConfiguration<ImportReceiptDetail>
{
    public void Configure(EntityTypeBuilder<ImportReceiptDetail> builder)
    {
        builder.ToTable("ImportReceiptDetails", table =>
        {
            table.HasCheckConstraint("CK_ImportReceiptDetails_Quantity", "[Quantity] > 0");
            table.HasCheckConstraint("CK_ImportReceiptDetails_UnitCost", "[UnitCost] > 0");
        });

        builder.HasKey(x => x.ImportReceiptDetailId);
        builder.Property(x => x.UnitCost).HasPrecision(18, 4);
        builder.HasIndex(x => new { x.ImportReceiptId, x.ProductId }).IsUnique();

        builder.HasOne(x => x.ImportReceipt)
            .WithMany(x => x.Details)
            .HasForeignKey(x => x.ImportReceiptId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Product)
            .WithMany(x => x.ImportReceiptDetails)
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ExportReceiptConfiguration : IEntityTypeConfiguration<ExportReceipt>
{
    public void Configure(EntityTypeBuilder<ExportReceipt> builder)
    {
        builder.ToTable("ExportReceipts");
        builder.HasKey(x => x.ExportReceiptId);
        builder.Property(x => x.ReceiptNumber).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.CreatedById).HasMaxLength(450).IsRequired();
        builder.Property(x => x.ApprovedById).HasMaxLength(450);
        builder.Property(x => x.CompletedById).HasMaxLength(450);
        builder.Property(x => x.RejectionReason).HasMaxLength(1000);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSDATETIMEOFFSET()");
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => x.ReceiptNumber).IsUnique();
        builder.HasIndex(x => new { x.Status, x.CreatedAt });

        builder.HasOne(x => x.Order)
            .WithMany(x => x.ExportReceipts)
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Warehouse)
            .WithMany(x => x.ExportReceipts)
            .HasForeignKey(x => x.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ApprovedById)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CompletedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ExportReceiptDetailConfiguration : IEntityTypeConfiguration<ExportReceiptDetail>
{
    public void Configure(EntityTypeBuilder<ExportReceiptDetail> builder)
    {
        builder.ToTable("ExportReceiptDetails", table =>
        {
            table.HasCheckConstraint("CK_ExportReceiptDetails_Quantity", "[Quantity] > 0");
            table.HasCheckConstraint("CK_ExportReceiptDetails_UnitCost", "[UnitCost] >= 0");
        });

        builder.HasKey(x => x.ExportReceiptDetailId);
        builder.Property(x => x.UnitCost).HasPrecision(18, 4);
        builder.HasIndex(x => new { x.ExportReceiptId, x.ProductId }).IsUnique();

        builder.HasOne(x => x.ExportReceipt)
            .WithMany(x => x.Details)
            .HasForeignKey(x => x.ExportReceiptId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Product)
            .WithMany(x => x.ExportReceiptDetails)
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
