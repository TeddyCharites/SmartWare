using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartWare.Domain.Entities;
using SmartWare.Infrastructure.Identity;

namespace SmartWare.Infrastructure.Data.Configurations;

internal sealed class InventoryConfiguration : IEntityTypeConfiguration<Inventory>
{
    public void Configure(EntityTypeBuilder<Inventory> builder)
    {
        builder.ToTable("Inventories", table =>
        {
            table.HasCheckConstraint("CK_Inventories_CurrentQuantity", "[CurrentQuantity] >= 0");
            table.HasCheckConstraint("CK_Inventories_ReservedQuantity", "[ReservedQuantity] >= 0");
            table.HasCheckConstraint(
                "CK_Inventories_AvailableQuantity",
                "[ReservedQuantity] <= [CurrentQuantity]");
            table.HasCheckConstraint("CK_Inventories_AverageCost", "[AverageCost] >= 0");
        });

        builder.HasKey(x => x.InventoryId);
        builder.Property(x => x.AverageCost).HasPrecision(18, 4);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new { x.ProductId, x.WarehouseId }).IsUnique();

        builder.HasOne(x => x.Product)
            .WithMany(x => x.Inventories)
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Warehouse)
            .WithMany(x => x.Inventories)
            .HasForeignKey(x => x.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StockReservationConfiguration : IEntityTypeConfiguration<StockReservation>
{
    public void Configure(EntityTypeBuilder<StockReservation> builder)
    {
        builder.ToTable("StockReservations", table =>
            table.HasCheckConstraint("CK_StockReservations_Quantity", "[Quantity] > 0"));

        builder.HasKey(x => x.StockReservationId);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSDATETIMEOFFSET()");
        builder.HasIndex(x => new { x.ExportReceiptId, x.ProductId }).IsUnique();
        builder.HasIndex(x => new { x.Status, x.ProductId });

        builder.HasOne(x => x.ExportReceipt)
            .WithMany(x => x.Reservations)
            .HasForeignKey(x => x.ExportReceiptId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Product)
            .WithMany(x => x.StockReservations)
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Warehouse)
            .WithMany(x => x.StockReservations)
            .HasForeignKey(x => x.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class InventoryTransactionConfiguration : IEntityTypeConfiguration<InventoryTransaction>
{
    public void Configure(EntityTypeBuilder<InventoryTransaction> builder)
    {
        builder.ToTable("InventoryTransactions", table =>
        {
            table.HasCheckConstraint("CK_InventoryTransactions_Quantity", "[Quantity] > 0");
            table.HasCheckConstraint("CK_InventoryTransactions_UnitCost", "[UnitCost] >= 0");
        });

        builder.HasKey(x => x.InventoryTransactionId);
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(x => x.UnitCost).HasPrecision(18, 4);
        builder.Property(x => x.ReferenceType).HasMaxLength(50).IsRequired();
        builder.Property(x => x.PerformedById).HasMaxLength(450).IsRequired();
        builder.Property(x => x.OccurredAt).HasDefaultValueSql("SYSDATETIMEOFFSET()");
        builder.HasIndex(x => new { x.ProductId, x.OccurredAt });
        builder.HasIndex(x => new { x.ReferenceType, x.ReferenceId });

        builder.HasOne(x => x.Product)
            .WithMany(x => x.InventoryTransactions)
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Warehouse)
            .WithMany(x => x.InventoryTransactions)
            .HasForeignKey(x => x.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.PerformedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
