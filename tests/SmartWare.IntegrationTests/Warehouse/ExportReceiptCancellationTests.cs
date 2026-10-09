using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartWare.Application.Warehouse.Exports;
using SmartWare.Domain.Entities;
using SmartWare.Domain.Enums;
using SmartWare.Infrastructure;
using SmartWare.Infrastructure.Data;
using SmartWare.Infrastructure.Identity;
using WarehouseEntity = SmartWare.Domain.Entities.Warehouse;

namespace SmartWare.IntegrationTests.Warehouse;

/// <summary>
/// Runs the export workflow against a throw-away SQL Server LocalDB database so the
/// Serializable transactions and RowVersion checks are exercised for real.
/// </summary>
public sealed class ExportReceiptCancellationTests : IAsyncLifetime
{
    private readonly ServiceProvider _services;
    private string _userId = string.Empty;
    private int _warehouseId;
    private int _productId;

    public ExportReceiptCancellationTests()
    {
        var databaseName = $"SmartWareTest_{Guid.NewGuid():N}";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        _services = services.BuildServiceProvider();
    }

    public async Task InitializeAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();

        var now = DateTimeOffset.UtcNow;
        var user = new ApplicationUser
        {
            UserName = "manager@test.local",
            Email = "manager@test.local",
            FullName = "Quản lý kiểm thử",
            CreatedAt = now
        };
        var warehouse = new WarehouseEntity { Code = "WH-T", Name = "Kho kiểm thử" };
        var product = new Product
        {
            Sku = "TEST-001",
            Name = "Sản phẩm kiểm thử",
            Category = new Category { Name = "Danh mục kiểm thử", CreatedAt = now },
            Supplier = new Supplier { Code = "NCC-T", Name = "NCC kiểm thử", CreatedAt = now },
            UnitOfMeasure = "Cái",
            SellingPrice = 100_000,
            MinStock = 1,
            MaxStock = 50,
            CreatedAt = now
        };
        db.Users.Add(user);
        db.Inventories.Add(new Inventory
        {
            Product = product,
            Warehouse = warehouse,
            CurrentQuantity = 10,
            AverageCost = 70_000
        });
        await db.SaveChangesAsync();

        _userId = user.Id;
        _warehouseId = warehouse.WarehouseId;
        _productId = product.ProductId;
    }

    public async Task DisposeAsync()
    {
        await using (var scope = _services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureDeletedAsync();
        }

        await _services.DisposeAsync();
    }

    [Fact]
    public async Task CancelApprovedReceipt_ReleasesReservedStock()
    {
        var receiptId = await CreateReceiptAsync(quantity: 4);
        var approve = await RunAsync(async service => await service.ReviewAsync(
            new ReviewExportReceiptCommand(receiptId, await RowVersionAsync(service, receiptId), true, null, _userId)));
        Assert.True(approve.Succeeded, string.Join("; ", approve.Errors));
        Assert.Equal((10, 4), await InventoryAsync());

        var cancel = await RunAsync(async service => await service.CancelAsync(
            new CancelExportReceiptCommand(receiptId, await RowVersionAsync(service, receiptId), "Khách hủy đơn", _userId)));

        Assert.True(cancel.Succeeded, string.Join("; ", cancel.Errors));
        Assert.Equal((10, 0), await InventoryAsync());
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var receipt = await db.ExportReceipts.Include(item => item.Reservations)
            .SingleAsync(item => item.ExportReceiptId == receiptId);
        Assert.Equal(ReceiptStatus.Cancelled, receipt.Status);
        Assert.Equal("Khách hủy đơn", receipt.RejectionReason);
        var reservation = Assert.Single(receipt.Reservations);
        Assert.Equal(ReservationStatus.Released, reservation.Status);
        Assert.NotNull(reservation.ReleasedAt);
    }

    [Fact]
    public async Task CancelPendingReceipt_DoesNotTouchInventory()
    {
        var receiptId = await CreateReceiptAsync(quantity: 3);

        var cancel = await RunAsync(async service => await service.CancelAsync(
            new CancelExportReceiptCommand(receiptId, await RowVersionAsync(service, receiptId), "Lập nhầm", _userId)));

        Assert.True(cancel.Succeeded, string.Join("; ", cancel.Errors));
        Assert.Equal((10, 0), await InventoryAsync());
    }

    [Fact]
    public async Task CancelCompletedReceipt_IsRejected()
    {
        var receiptId = await CreateReceiptAsync(quantity: 2);
        await RunAsync(async service => await service.ReviewAsync(
            new ReviewExportReceiptCommand(receiptId, await RowVersionAsync(service, receiptId), true, null, _userId)));
        var complete = await RunAsync(async service => await service.CompleteAsync(
            new CompleteExportReceiptCommand(receiptId, await RowVersionAsync(service, receiptId), _userId)));
        Assert.True(complete.Succeeded, string.Join("; ", complete.Errors));

        var cancel = await RunAsync(async service => await service.CancelAsync(
            new CancelExportReceiptCommand(receiptId, await RowVersionAsync(service, receiptId), "Thử hủy", _userId)));

        Assert.False(cancel.Succeeded);
        Assert.Equal((8, 0), await InventoryAsync());
    }

    [Fact]
    public async Task CancelWithStaleRowVersion_IsRejected()
    {
        var receiptId = await CreateReceiptAsync(quantity: 1);
        var staleVersion = await RunAsync(service => RowVersionAsync(service, receiptId));
        await RunAsync(async service => await service.ReviewAsync(
            new ReviewExportReceiptCommand(receiptId, staleVersion, true, null, _userId)));

        var cancel = await RunAsync(async service => await service.CancelAsync(
            new CancelExportReceiptCommand(receiptId, staleVersion, "Phiên bản cũ", _userId)));

        Assert.False(cancel.Succeeded);
        Assert.Equal((10, 1), await InventoryAsync());
    }

    private async Task<int> CreateReceiptAsync(int quantity)
    {
        var result = await RunAsync(service => service.CreateAsync(new CreateExportReceiptCommand(
            null,
            _warehouseId,
            [new CreateExportReceiptLine(_productId, quantity)],
            _userId)));
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));

        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().ExportReceipts
            .OrderByDescending(receipt => receipt.ExportReceiptId)
            .Select(receipt => receipt.ExportReceiptId)
            .FirstAsync();
    }

    private static async Task<string> RowVersionAsync(IExportReceiptService service, int receiptId) =>
        (await service.GetByIdAsync(receiptId))!.RowVersion;

    private async Task<T> RunAsync<T>(Func<IExportReceiptService, Task<T>> action)
    {
        await using var scope = _services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IExportReceiptService>());
    }

    private async Task<(int Current, int Reserved)> InventoryAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        var inventory = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Inventories
            .AsNoTracking()
            .SingleAsync(item => item.ProductId == _productId && item.WarehouseId == _warehouseId);
        return (inventory.CurrentQuantity, inventory.ReservedQuantity);
    }
}
