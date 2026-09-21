using Microsoft.EntityFrameworkCore;
using SmartWare.Domain.Entities;
using SmartWare.Infrastructure.Data;

namespace SmartWare.IntegrationTests.Data;

public sealed class ApplicationDbContextModelTests
{
    [Fact]
    public void Model_ContainsCoreWarehouseEntities()
    {
        using var context = CreateContext();

        Assert.NotNull(context.Model.FindEntityType(typeof(Product)));
        Assert.NotNull(context.Model.FindEntityType(typeof(Inventory)));
        Assert.NotNull(context.Model.FindEntityType(typeof(ImportReceipt)));
        Assert.NotNull(context.Model.FindEntityType(typeof(ExportReceipt)));
        Assert.NotNull(context.Model.FindEntityType(typeof(StockReservation)));
        Assert.NotNull(context.Model.FindEntityType(typeof(InventoryTransaction)));
        Assert.NotNull(context.Model.FindEntityType(typeof(KnowledgeDocument)));
        Assert.NotNull(context.Model.FindEntityType(typeof(KnowledgeChunk)));
        Assert.NotNull(context.Model.FindEntityType(typeof(ChatSession)));
        Assert.NotNull(context.Model.FindEntityType(typeof(ChatMessage)));
    }

    [Fact]
    public void Inventory_HasUniqueProductWarehouseIndex()
    {
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(Inventory));

        var index = Assert.Single(
            entityType!.GetIndexes(),
            candidate => candidate.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(Inventory.ProductId), nameof(Inventory.WarehouseId)]));

        Assert.True(index.IsUnique);
    }

    [Fact]
    public void ChatMessage_BelongsToSessionWithCascadeDelete()
    {
        using var context = CreateContext();
        var message = context.Model.FindEntityType(typeof(ChatMessage));

        var foreignKey = Assert.Single(message!.GetForeignKeys());
        Assert.Equal(typeof(ChatSession), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=SmartWareModelTests;Trusted_Connection=True")
            .Options;

        return new ApplicationDbContext(options);
    }
}
