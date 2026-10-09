using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartWare.Application.AI.Chatbot;
using SmartWare.Application.Warehouse.Imports;
using SmartWare.Domain.Entities;
using SmartWare.Domain.Enums;
using SmartWare.Infrastructure;
using SmartWare.Infrastructure.AI.Tools;
using SmartWare.Infrastructure.Data;
using SmartWare.Infrastructure.Identity;
using WarehouseEntity = SmartWare.Domain.Entities.Warehouse;

namespace SmartWare.IntegrationTests.AI;

/// <summary>
/// Drafts are built against a throw-away SQL Server LocalDB database because product lookup
/// relies on SQL Server's accent-insensitive collation.
/// </summary>
public sealed class ReceiptDraftBuilderTests : IAsyncLifetime
{
    private readonly ServiceProvider _services;
    private string _userId = string.Empty;

    public ReceiptDraftBuilderTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    $"Server=(localdb)\\MSSQLLocalDB;Database=SmartWareTest_{Guid.NewGuid():N};Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
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
        var user = new ApplicationUser { UserName = "staff@test.local", Email = "staff@test.local", FullName = "Nhân viên", CreatedAt = now };
        var warehouse = new WarehouseEntity { Code = "WH-T", Name = "Kho kiểm thử" };
        var category = new Category { Name = "Linh kiện", CreatedAt = now };
        var componentSupplier = new Supplier { Code = "NCC-LK", Name = "Linh Kiện Việt", CreatedAt = now };
        var displaySupplier = new Supplier { Code = "NCC-MH", Name = "Màn Hình Sao Việt", CreatedAt = now };
        Product NewProduct(string sku, string name, Supplier supplier) => new()
        {
            Sku = sku, Name = name, Category = category, Supplier = supplier,
            UnitOfMeasure = "Chiếc", SellingPrice = 1, MinStock = 1, MaxStock = 100, CreatedAt = now
        };
        var ram = NewProduct("T-RAM", "RAM DDR5 16GB", componentSupplier);
        var ssd = NewProduct("T-SSD", "Ổ cứng SSD NVMe 1TB", componentSupplier);
        var monitor = NewProduct("T-MON", "Màn hình IPS 24 inch", displaySupplier);
        var curved = NewProduct("T-MON2", "Màn hình cong 27 inch", displaySupplier);

        db.Users.Add(user);
        db.Inventories.AddRange(
            new Inventory { Product = ram, Warehouse = warehouse, CurrentQuantity = 10, AverageCost = 900_000 },
            new Inventory { Product = ssd, Warehouse = warehouse, CurrentQuantity = 5, ReservedQuantity = 2, AverageCost = 1_500_000 },
            new Inventory { Product = monitor, Warehouse = warehouse, CurrentQuantity = 4, AverageCost = 2_600_000 },
            new Inventory { Product = curved, Warehouse = warehouse, CurrentQuantity = 2, AverageCost = 4_000_000 });
        await db.SaveChangesAsync();

        // A previous completed import sets the "last import price" for RAM.
        db.ImportReceipts.Add(new ImportReceipt
        {
            ReceiptNumber = "PN-TEST-1",
            SupplierId = componentSupplier.SupplierId,
            WarehouseId = warehouse.WarehouseId,
            Status = ReceiptStatus.Completed,
            CreatedById = user.Id,
            CreatedAt = now.AddDays(-3),
            Details = [new ImportReceiptDetail { ProductId = ram.ProductId, Quantity = 10, UnitCost = 1_000_000 }]
        });
        await db.SaveChangesAsync();
        _userId = user.Id;
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
    public async Task ImportDraft_ResolvesProductsInfersSupplierAndUsesLastImportPrice()
    {
        var draft = await BuildImportAsync(null, [new("ram ddr5", 50, null), new("T-SSD", 20, 1_600_000)]);

        Assert.True(draft.CanConfirm, string.Join(" | ", draft.Warnings));
        Assert.Equal("Linh Kiện Việt", draft.SupplierName);
        Assert.Equal("Kho kiểm thử", draft.WarehouseName);
        Assert.Collection(
            draft.Lines,
            line => { Assert.Equal("T-RAM", line.Sku); Assert.Equal(50, line.Quantity); Assert.Equal(1_000_000, line.UnitCost); },
            line => { Assert.Equal("T-SSD", line.Sku); Assert.Equal(1_600_000, line.UnitCost); });
        Assert.Equal(50 * 1_000_000m + 20 * 1_600_000m, draft.TotalValue);
        Assert.Contains(draft.Warnings, warning => warning.Contains("lần nhập gần nhất"));
    }

    [Fact]
    public async Task ImportDraft_BlocksProductsFromDifferentSuppliers()
    {
        var draft = await BuildImportAsync(null, [new("T-RAM", 5, null), new("T-MON", 2, null)]);

        Assert.False(draft.CanConfirm);
        Assert.Contains(draft.Warnings, warning => warning.Contains("nhà cung cấp khác nhau"));
    }

    [Fact]
    public async Task ImportDraft_ReportsAmbiguousProductInsteadOfGuessing()
    {
        var draft = await BuildImportAsync("Màn Hình Sao Việt", [new("màn hình", 3, null)]);

        Assert.False(draft.CanConfirm);
        Assert.Empty(draft.Lines);
        var warning = Assert.Single(draft.Warnings, item => item.Contains("khớp nhiều sản phẩm"));
        Assert.Contains("T-MON", warning);
        Assert.Contains("T-MON2", warning);
    }

    [Fact]
    public async Task ExportDraft_BlocksQuantityAboveAvailableStock()
    {
        await using var scope = _services.CreateAsyncScope();
        var builder = new ReceiptDraftBuilder(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());

        var tooMany = await builder.BuildExportAsync(null, null, [new("ssd", 4, null)], CancellationToken.None);
        var enough = await builder.BuildExportAsync(null, null, [new("ssd", 3, null)], CancellationToken.None);

        Assert.False(tooMany.CanConfirm);
        Assert.Equal(3, Assert.Single(tooMany.Lines).AvailableQuantity);
        Assert.True(enough.CanConfirm, string.Join(" | ", enough.Warnings));
    }

    [Fact]
    public async Task ConfirmedImportDraft_CreatesPendingReceiptWithoutChangingStock()
    {
        var draft = await BuildImportAsync(null, [new("T-RAM", 7, 950_000)]);
        Assert.True(draft.CanConfirm);

        // Same command the "Xác nhận tạo phiếu" endpoint sends to the regular service.
        await using (var scope = _services.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<IImportReceiptService>().CreateAsync(
                new CreateImportReceiptCommand(
                    draft.SupplierId!.Value,
                    draft.WarehouseId,
                    draft.Lines.Select(line => new CreateImportReceiptLine(line.ProductId, line.Quantity, line.UnitCost)).ToArray(),
                    _userId));
            Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        }

        await using var check = _services.CreateAsyncScope();
        var db = check.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var receipt = await db.ImportReceipts.Include(item => item.Details)
            .OrderByDescending(item => item.ImportReceiptId)
            .FirstAsync();
        Assert.Equal(ReceiptStatus.Pending, receipt.Status);
        Assert.Equal(7, Assert.Single(receipt.Details).Quantity);
        var ramStock = await db.Inventories.AsNoTracking().SingleAsync(item => item.Product.Sku == "T-RAM");
        Assert.Equal(10, ramStock.CurrentQuantity);
    }

    private async Task<ChatReceiptDraft> BuildImportAsync(string? supplier, IReadOnlyList<DraftLineRequest> lines)
    {
        await using var scope = _services.CreateAsyncScope();
        var builder = new ReceiptDraftBuilder(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
        return await builder.BuildImportAsync(supplier, null, lines, CancellationToken.None);
    }
}
