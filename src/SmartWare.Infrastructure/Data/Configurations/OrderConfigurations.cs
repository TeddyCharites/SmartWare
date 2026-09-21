using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartWare.Domain.Entities;
using SmartWare.Infrastructure.Identity;

namespace SmartWare.Infrastructure.Data.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders", table =>
            table.HasCheckConstraint("CK_Orders_TotalAmount", "[TotalAmount] >= 0"));

        builder.HasKey(x => x.OrderId);
        builder.Property(x => x.OrderNumber).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.TotalAmount).HasPrecision(18, 2);
        builder.Property(x => x.OrderDate).HasDefaultValueSql("SYSDATETIMEOFFSET()");
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSDATETIMEOFFSET()");
        builder.Property(x => x.CreatedById).HasMaxLength(450).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => x.OrderNumber).IsUnique();
        builder.HasIndex(x => new { x.Status, x.OrderDate });

        builder.HasOne(x => x.Customer)
            .WithMany(x => x.Orders)
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class OrderDetailConfiguration : IEntityTypeConfiguration<OrderDetail>
{
    public void Configure(EntityTypeBuilder<OrderDetail> builder)
    {
        builder.ToTable("OrderDetails", table =>
        {
            table.HasCheckConstraint("CK_OrderDetails_Quantity", "[Quantity] > 0");
            table.HasCheckConstraint("CK_OrderDetails_UnitPrice", "[UnitPrice] >= 0");
        });

        builder.HasKey(x => x.OrderDetailId);
        builder.Property(x => x.UnitPrice).HasPrecision(18, 2);
        builder.HasIndex(x => new { x.OrderId, x.ProductId }).IsUnique();

        builder.HasOne(x => x.Order)
            .WithMany(x => x.Details)
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Product)
            .WithMany(x => x.OrderDetails)
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
