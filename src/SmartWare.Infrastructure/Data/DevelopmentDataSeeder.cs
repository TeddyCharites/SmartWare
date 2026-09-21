using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartWare.Domain.Entities;
using SmartWare.Domain.Enums;
using SmartWare.Infrastructure.Identity;

namespace SmartWare.Infrastructure.Data;

public static class DevelopmentDataSeeder
{
    private const string DemoSkuPrefix = "DEMO-";

    private static readonly IReadOnlyDictionary<string, string> DemoImageUrls =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["DEMO-LAP-001"] = "/images/products/demo-laptop.jpg",
            ["DEMO-MON-001"] = "/images/products/demo-monitor.jpg",
            ["DEMO-PRN-001"] = "/images/products/demo-printer.jpg",
            ["DEMO-SSD-001"] = "/images/products/demo-ssd.jpg",
            ["DEMO-RAM-001"] = "/images/products/demo-ram.jpg",
            ["DEMO-RTR-001"] = "/images/products/demo-router.jpg",
            ["DEMO-SWT-001"] = "/images/products/demo-switch.jpg",
            ["DEMO-UPS-001"] = "/images/products/demo-ups.jpg",
            ["DEMO-KBD-001"] = "/images/products/demo-keyboard.jpg",
            ["DEMO-MSE-001"] = "/images/products/demo-mouse.jpg",
            ["DEMO-CAB-001"] = "/images/products/demo-cable.jpg"
        };

    public static async Task SeedAsync(
        IServiceProvider services,
        IConfiguration configuration,
        bool isDevelopment)
    {
        if (!isDevelopment || !configuration.GetValue("DevelopmentSeedData:Enabled", true))
        {
            return;
        }

        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("DevelopmentDataSeeder");

        var existingDemoProducts = await dbContext.Products
            .Where(product => product.Sku.StartsWith(DemoSkuPrefix))
            .ToListAsync();
        if (existingDemoProducts.Count > 0)
        {
            var updatedImageCount = 0;
            foreach (var product in existingDemoProducts)
            {
                if (string.IsNullOrWhiteSpace(product.ImageUrl)
                    && DemoImageUrls.TryGetValue(product.Sku, out var imageUrl))
                {
                    product.ImageUrl = imageUrl;
                    updatedImageCount++;
                }
            }

            if (updatedImageCount > 0)
            {
                await dbContext.SaveChangesAsync();
                logger.LogInformation(
                    "Đã bổ sung ảnh cho {ProductCount} sản phẩm demo hiện có.",
                    updatedImageCount);
            }
            else
            {
                logger.LogInformation("Dữ liệu mẫu SmartWare đã tồn tại; bỏ qua bước seed.");
            }

            return;
        }

        var adminEmail = configuration["DevelopmentAdmin:Email"];
        var admin = string.IsNullOrWhiteSpace(adminEmail)
            ? null
            : await userManager.FindByEmailAsync(adminEmail);
        if (admin is null)
        {
            logger.LogWarning(
                "Không thể seed dữ liệu mẫu vì chưa có tài khoản DevelopmentAdmin hợp lệ.");
            return;
        }

        var warehouse = await dbContext.Warehouses
            .SingleOrDefaultAsync(item => item.Code == "WH-MAIN");
        if (warehouse is null)
        {
            logger.LogWarning("Không thể seed dữ liệu mẫu vì chưa có kho WH-MAIN.");
            return;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync();

        var now = DateTimeOffset.UtcNow;
        var office = new Category
        {
            Name = "Thiết bị văn phòng (Demo)",
            Description = "Máy tính, màn hình và thiết bị phục vụ văn phòng.",
            CreatedAt = now
        };
        var components = new Category
        {
            Name = "Linh kiện máy tính (Demo)",
            Description = "Ổ lưu trữ và linh kiện nâng cấp máy tính.",
            CreatedAt = now
        };
        var network = new Category
        {
            Name = "Thiết bị mạng (Demo)",
            Description = "Router, switch và thiết bị kết nối mạng.",
            CreatedAt = now
        };
        var accessories = new Category
        {
            Name = "Phụ kiện (Demo)",
            Description = "Phụ kiện máy tính và vật tư kết nối.",
            CreatedAt = now
        };

        var digitalSupplier = new Supplier
        {
            Code = "DEMO-NCC-001",
            Name = "Công ty Công nghệ Sao Việt",
            ContactName = "Nguyễn Minh Anh",
            Phone = "0901000001",
            Email = "kinhdoanh@saoviet.demo",
            Address = "Quận 1, TP. Hồ Chí Minh",
            CreatedAt = now
        };
        var componentSupplier = new Supplier
        {
            Code = "DEMO-NCC-002",
            Name = "Nhà phân phối Linh Kiện Việt",
            ContactName = "Trần Quốc Bảo",
            Phone = "0901000002",
            Email = "lienhe@linhkienviet.demo",
            Address = "Thành phố Thủ Đức, TP. Hồ Chí Minh",
            CreatedAt = now
        };
        var networkSupplier = new Supplier
        {
            Code = "DEMO-NCC-003",
            Name = "Công ty Giải pháp Mạng Đông Dương",
            ContactName = "Lê Thu Hà",
            Phone = "0901000003",
            Email = "sales@dongduongnet.demo",
            Address = "Quận Hải Châu, Đà Nẵng",
            CreatedAt = now
        };

        var products = new[]
        {
            Product("DEMO-LAP-001", "Laptop SmartBook Pro 14", office, digitalSupplier,
                "Chiếc", 21_990_000m, 5, 30, "Laptop văn phòng cấu hình cao."),
            Product("DEMO-MON-001", "Màn hình IPS 24 inch", office, digitalSupplier,
                "Chiếc", 3_690_000m, 8, 40, "Màn hình Full HD dùng cho văn phòng."),
            Product("DEMO-PRN-001", "Máy in laser hai mặt", office, digitalSupplier,
                "Chiếc", 5_490_000m, 3, 15, "Máy in laser hỗ trợ in hai mặt tự động."),
            Product("DEMO-SSD-001", "Ổ cứng SSD NVMe 1TB", components, componentSupplier,
                "Chiếc", 2_190_000m, 6, 35, "SSD NVMe dung lượng 1TB."),
            Product("DEMO-RAM-001", "RAM DDR5 16GB", components, componentSupplier,
                "Thanh", 1_390_000m, 10, 60, "RAM DDR5 dành cho máy tính để bàn."),
            Product("DEMO-RTR-001", "Router Wi-Fi 6 AX3000", network, networkSupplier,
                "Chiếc", 2_890_000m, 5, 25, "Router Wi-Fi 6 cho văn phòng vừa và nhỏ."),
            Product("DEMO-SWT-001", "Switch mạng 24 cổng Gigabit", network, networkSupplier,
                "Chiếc", 4_290_000m, 3, 15, "Switch quản lý 24 cổng Gigabit."),
            Product("DEMO-UPS-001", "Bộ lưu điện UPS 1000VA", network, networkSupplier,
                "Chiếc", 3_290_000m, 4, 20, "UPS bảo vệ máy tính và thiết bị mạng."),
            Product("DEMO-KBD-001", "Bàn phím không dây", accessories, componentSupplier,
                "Chiếc", 690_000m, 10, 60, "Bàn phím không dây dành cho văn phòng."),
            Product("DEMO-MSE-001", "Chuột không dây", accessories, componentSupplier,
                "Chiếc", 390_000m, 12, 80, "Chuột không dây yên tĩnh."),
            Product("DEMO-CAB-001", "Cáp mạng Cat6 hộp 305m", accessories, networkSupplier,
                "Hộp", 2_590_000m, 5, 25, "Cáp mạng Cat6 nguyên hộp 305 mét.")
        };

        var customers = new[]
        {
            Customer("DEMO-KH-001", "Công ty Nội thất An Gia", "0902000001", "muahang@angia.demo", now),
            Customer("DEMO-KH-002", "Trường Cao đẳng Kỹ thuật Thành Công", "0902000002", "thietbi@thanhcong.demo", now),
            Customer("DEMO-KH-003", "Công ty Logistics Biển Đông", "0902000003", "it@biendong.demo", now),
            Customer("DEMO-KH-004", "Văn phòng Luật Minh Tâm", "0902000004", "contact@minhtam.demo", now)
        };

        dbContext.AddRange(office, components, network, accessories);
        dbContext.AddRange(digitalSupplier, componentSupplier, networkSupplier);
        dbContext.AddRange(products);
        dbContext.AddRange(customers);
        await dbContext.SaveChangesAsync();

        var stock = new Dictionary<string, (int Quantity, int Reserved, decimal Cost)>
        {
            ["DEMO-LAP-001"] = (18, 0, 16_500_000m),
            ["DEMO-MON-001"] = (7, 2, 2_650_000m),
            ["DEMO-PRN-001"] = (8, 0, 4_100_000m),
            ["DEMO-SSD-001"] = (9, 0, 1_520_000m),
            ["DEMO-RAM-001"] = (42, 0, 920_000m),
            ["DEMO-RTR-001"] = (28, 0, 2_050_000m),
            ["DEMO-SWT-001"] = (4, 1, 3_150_000m),
            ["DEMO-UPS-001"] = (3, 0, 2_480_000m),
            ["DEMO-KBD-001"] = (36, 0, 410_000m),
            ["DEMO-MSE-001"] = (0, 0, 225_000m),
            ["DEMO-CAB-001"] = (20, 0, 1_880_000m)
        };
        var inventories = products.ToDictionary(
            product => product.Sku,
            product =>
            {
                var item = stock[product.Sku];
                return new Inventory
                {
                    ProductId = product.ProductId,
                    WarehouseId = warehouse.WarehouseId,
                    CurrentQuantity = item.Quantity,
                    ReservedQuantity = item.Reserved,
                    AverageCost = item.Cost
                };
            });
        dbContext.Inventories.AddRange(inventories.Values);
        await dbContext.SaveChangesAsync();

        var productBySku = products.ToDictionary(product => product.Sku);
        var historySkus = new[]
        {
            "DEMO-LAP-001", "DEMO-MON-001", "DEMO-SSD-001", "DEMO-RAM-001",
            "DEMO-RTR-001", "DEMO-KBD-001", "DEMO-MSE-001"
        };

        for (var monthIndex = 0; monthIndex < 6; monthIndex++)
        {
            var monthOffset = monthIndex - 5;
            var importDate = MonthDate(monthOffset, 5);
            var exportDate = MonthDate(monthOffset, 18);
            var importReceipt = new ImportReceipt
            {
                ReceiptNumber = $"DEMO-PN-HIST-{monthIndex + 1:00}",
                SupplierId = monthIndex % 2 == 0
                    ? componentSupplier.SupplierId
                    : digitalSupplier.SupplierId,
                WarehouseId = warehouse.WarehouseId,
                Status = ReceiptStatus.Completed,
                CreatedById = admin.Id,
                ApprovedById = admin.Id,
                CompletedById = admin.Id,
                CreatedAt = importDate.AddDays(-1),
                ApprovedAt = importDate.AddHours(-2),
                CompletedAt = importDate
            };
            foreach (var sku in historySkus)
            {
                var product = productBySku[sku];
                importReceipt.Details.Add(new ImportReceiptDetail
                {
                    ProductId = product.ProductId,
                    Quantity = 12 + monthIndex * 2,
                    UnitCost = stock[sku].Cost
                });
            }

            var customer = customers[monthIndex % customers.Length];
            var orderProducts = historySkus
                .Skip(monthIndex % 3)
                .Take(3)
                .Select(sku => productBySku[sku])
                .ToArray();
            var order = new Order
            {
                OrderNumber = $"DEMO-DH-HIST-{monthIndex + 1:00}",
                CustomerId = customer.CustomerId,
                Status = OrderStatus.Completed,
                OrderDate = exportDate.AddDays(-2),
                CompletedAt = exportDate,
                CreatedById = admin.Id,
                CreatedAt = exportDate.AddDays(-2)
            };
            foreach (var product in orderProducts)
            {
                order.Details.Add(new OrderDetail
                {
                    ProductId = product.ProductId,
                    Quantity = 2 + monthIndex,
                    UnitPrice = product.SellingPrice
                });
            }

            order.TotalAmount = order.Details.Sum(detail => detail.Quantity * detail.UnitPrice);
            var exportReceipt = new ExportReceipt
            {
                ReceiptNumber = $"DEMO-PX-HIST-{monthIndex + 1:00}",
                Order = order,
                WarehouseId = warehouse.WarehouseId,
                Status = ReceiptStatus.Completed,
                CreatedById = admin.Id,
                ApprovedById = admin.Id,
                CompletedById = admin.Id,
                CreatedAt = exportDate.AddDays(-1),
                ApprovedAt = exportDate.AddHours(-2),
                CompletedAt = exportDate
            };
            foreach (var detail in order.Details)
            {
                var product = products.Single(item => item.ProductId == detail.ProductId);
                exportReceipt.Details.Add(new ExportReceiptDetail
                {
                    ProductId = detail.ProductId,
                    Quantity = detail.Quantity,
                    UnitCost = stock[product.Sku].Cost
                });
            }

            dbContext.Add(importReceipt);
            dbContext.Add(exportReceipt);
            await dbContext.SaveChangesAsync();

            dbContext.InventoryTransactions.AddRange(importReceipt.Details.Select(detail =>
                new InventoryTransaction
                {
                    ProductId = detail.ProductId,
                    WarehouseId = warehouse.WarehouseId,
                    Type = InventoryTransactionType.In,
                    Quantity = detail.Quantity,
                    UnitCost = detail.UnitCost,
                    OccurredAt = importDate,
                    ReferenceType = nameof(ImportReceipt),
                    ReferenceId = importReceipt.ImportReceiptId,
                    PerformedById = admin.Id
                }));
            dbContext.InventoryTransactions.AddRange(exportReceipt.Details.Select(detail =>
                new InventoryTransaction
                {
                    ProductId = detail.ProductId,
                    WarehouseId = warehouse.WarehouseId,
                    Type = InventoryTransactionType.Out,
                    Quantity = detail.Quantity,
                    UnitCost = detail.UnitCost,
                    OccurredAt = exportDate,
                    ReferenceType = nameof(ExportReceipt),
                    ReferenceId = exportReceipt.ExportReceiptId,
                    PerformedById = admin.Id
                }));
        }

        var pendingImport = new ImportReceipt
        {
            ReceiptNumber = "DEMO-PN-PENDING-01",
            SupplierId = networkSupplier.SupplierId,
            WarehouseId = warehouse.WarehouseId,
            Status = ReceiptStatus.Pending,
            CreatedById = admin.Id,
            CreatedAt = now.AddDays(-1),
            Details =
            {
                new ImportReceiptDetail
                {
                    ProductId = productBySku["DEMO-MSE-001"].ProductId,
                    Quantity = 50,
                    UnitCost = stock["DEMO-MSE-001"].Cost
                },
                new ImportReceiptDetail
                {
                    ProductId = productBySku["DEMO-UPS-001"].ProductId,
                    Quantity = 12,
                    UnitCost = stock["DEMO-UPS-001"].Cost
                }
            }
        };

        var processingOrder = new Order
        {
            OrderNumber = "DEMO-DH-PROCESSING-01",
            CustomerId = customers[0].CustomerId,
            Status = OrderStatus.Processing,
            OrderDate = now.AddDays(-2),
            CreatedById = admin.Id,
            CreatedAt = now.AddDays(-2),
            Details =
            {
                new OrderDetail
                {
                    ProductId = productBySku["DEMO-MON-001"].ProductId,
                    Quantity = 2,
                    UnitPrice = productBySku["DEMO-MON-001"].SellingPrice
                },
                new OrderDetail
                {
                    ProductId = productBySku["DEMO-SWT-001"].ProductId,
                    Quantity = 1,
                    UnitPrice = productBySku["DEMO-SWT-001"].SellingPrice
                }
            }
        };
        processingOrder.TotalAmount = processingOrder.Details.Sum(item => item.Quantity * item.UnitPrice);
        var approvedExport = new ExportReceipt
        {
            ReceiptNumber = "DEMO-PX-APPROVED-01",
            Order = processingOrder,
            WarehouseId = warehouse.WarehouseId,
            Status = ReceiptStatus.Approved,
            CreatedById = admin.Id,
            ApprovedById = admin.Id,
            CreatedAt = now.AddDays(-1),
            ApprovedAt = now.AddHours(-8),
            Details =
            {
                new ExportReceiptDetail
                {
                    ProductId = productBySku["DEMO-MON-001"].ProductId,
                    Quantity = 2,
                    UnitCost = stock["DEMO-MON-001"].Cost
                },
                new ExportReceiptDetail
                {
                    ProductId = productBySku["DEMO-SWT-001"].ProductId,
                    Quantity = 1,
                    UnitCost = stock["DEMO-SWT-001"].Cost
                }
            }
        };
        approvedExport.Reservations.Add(new StockReservation
        {
            ProductId = productBySku["DEMO-MON-001"].ProductId,
            WarehouseId = warehouse.WarehouseId,
            Quantity = 2,
            Status = ReservationStatus.Active,
            CreatedAt = now.AddHours(-8)
        });
        approvedExport.Reservations.Add(new StockReservation
        {
            ProductId = productBySku["DEMO-SWT-001"].ProductId,
            WarehouseId = warehouse.WarehouseId,
            Quantity = 1,
            Status = ReservationStatus.Active,
            CreatedAt = now.AddHours(-8)
        });

        var pendingOrder = new Order
        {
            OrderNumber = "DEMO-DH-PENDING-01",
            CustomerId = customers[2].CustomerId,
            Status = OrderStatus.Pending,
            OrderDate = now,
            CreatedById = admin.Id,
            CreatedAt = now,
            Details =
            {
                new OrderDetail
                {
                    ProductId = productBySku["DEMO-LAP-001"].ProductId,
                    Quantity = 3,
                    UnitPrice = productBySku["DEMO-LAP-001"].SellingPrice
                },
                new OrderDetail
                {
                    ProductId = productBySku["DEMO-KBD-001"].ProductId,
                    Quantity = 3,
                    UnitPrice = productBySku["DEMO-KBD-001"].SellingPrice
                }
            }
        };
        pendingOrder.TotalAmount = pendingOrder.Details.Sum(item => item.Quantity * item.UnitPrice);

        dbContext.Add(pendingImport);
        dbContext.Add(approvedExport);
        dbContext.Add(pendingOrder);
        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();

        logger.LogInformation(
            "Đã seed dữ liệu mẫu SmartWare: {ProductCount} sản phẩm, {CustomerCount} khách hàng, " +
            "6 tháng lịch sử nhập/xuất và các chứng từ chờ xử lý.",
            products.Length,
            customers.Length);
    }

    private static Product Product(
        string sku,
        string name,
        Category category,
        Supplier supplier,
        string unit,
        decimal price,
        int minimumStock,
        int maximumStock,
        string description) => new()
        {
            Sku = sku,
            Name = name,
            Category = category,
            Supplier = supplier,
            UnitOfMeasure = unit,
            SellingPrice = price,
            ImageUrl = DemoImageUrls.GetValueOrDefault(sku),
            MinStock = minimumStock,
            MaxStock = maximumStock,
            Description = description,
            CreatedAt = DateTimeOffset.UtcNow
        };

    private static Customer Customer(
        string code,
        string name,
        string phone,
        string email,
        DateTimeOffset createdAt) => new()
        {
            Code = code,
            Name = name,
            Phone = phone,
            Email = email,
            Address = "TP. Hồ Chí Minh",
            CreatedAt = createdAt
        };

    private static DateTimeOffset MonthDate(int monthOffset, int day)
    {
        var month = DateTime.UtcNow.Date.AddMonths(monthOffset);
        return new DateTimeOffset(
            new DateTime(month.Year, month.Month, day, 9, 0, 0, DateTimeKind.Utc));
    }
}
