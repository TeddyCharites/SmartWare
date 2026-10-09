using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartWare.Application.AI.Chatbot;
using SmartWare.Application.AI.Rag;
using SmartWare.Application.Identity;
using SmartWare.Application.Dashboard;
using SmartWare.Application.Catalog.Categories;
using SmartWare.Application.Catalog.Products;
using SmartWare.Application.Catalog.Suppliers;
using SmartWare.Application.Catalog.Customers;
using SmartWare.Application.Warehouse.Imports;
using SmartWare.Application.Warehouse.Exports;
using SmartWare.Application.Warehouse.Inventory;
using SmartWare.Application.Sales.Orders;
using SmartWare.Application.Reports;
using SmartWare.Domain.Constants;
using SmartWare.Infrastructure.Catalog;
using SmartWare.Infrastructure.Data;
using SmartWare.Infrastructure.Dashboard;
using SmartWare.Infrastructure.Identity;
using SmartWare.Infrastructure.InventoryOperations;
using SmartWare.Infrastructure.Sales;
using SmartWare.Infrastructure.Reports;
using SmartWare.Infrastructure.AI;
using SmartWare.Infrastructure.AI.Tools;

namespace SmartWare.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Không tìm thấy connection string 'DefaultConnection'.");

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(
                connectionString,
                sqlOptions => sqlOptions.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName)));

        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.SignIn.RequireConfirmedAccount = false;
            })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = "/tai-khoan/dang-nhap";
            options.AccessDeniedPath = "/tai-khoan/tu-choi-truy-cap";
            options.Cookie.Name = "SmartWare.Auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
        });

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthorizationPolicies.AdminOnly,
                policy => policy.RequireRole(RoleNames.Admin))
            .AddPolicy(AuthorizationPolicies.ManagerOnly,
                policy => policy.RequireRole(RoleNames.Manager))
            .AddPolicy(AuthorizationPolicies.CreateReceipts,
                policy => policy.RequireRole(RoleNames.Admin, RoleNames.Employee))
            .AddPolicy(AuthorizationPolicies.ApproveReceipts,
                policy => policy.RequireRole(RoleNames.Manager))
            .AddPolicy(AuthorizationPolicies.CompleteReceipts,
                policy => policy.RequireRole(RoleNames.Admin, RoleNames.Employee))
            .AddPolicy(AuthorizationPolicies.AdvancedReports,
                policy => policy.RequireRole(RoleNames.Admin, RoleNames.Manager));

        services.AddScoped<IUserManagementService, UserManagementService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<ISupplierService, SupplierService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IImportReceiptService, ImportReceiptService>();
        services.AddScoped<IExportReceiptService, ExportReceiptService>();
        services.AddScoped<IInventoryQueryService, InventoryQueryService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IChatHistoryService, ChatHistoryService>();
        services.AddScoped<IKnowledgeService, KnowledgeService>();
        services.AddScoped<IWarehouseChatTools, WarehouseChatTools>();
        services.AddMemoryCache();
        services.AddSingleton<IChatDraftStore, ChatDraftStore>();
        services.Configure<GeminiOptions>(options =>
        {
            configuration.GetSection(GeminiOptions.SectionName).Bind(options);
            options.ApiKey ??= configuration["GEMINI_API_KEY"];
        });
        services.AddHttpClient<IGeminiService, GeminiService>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<GeminiOptions>>().Value;
            var endpoint = Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var configuredEndpoint)
                ? configuredEndpoint
                : new Uri("https://generativelanguage.googleapis.com/v1beta/");
            client.BaseAddress = endpoint.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
                ? endpoint
                : new Uri(endpoint.AbsoluteUri + '/');
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 5, 120));
        });
        services.AddHttpClient<IGeminiEmbeddingService, GeminiEmbeddingService>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<GeminiOptions>>().Value;
            var endpoint = Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var configuredEndpoint)
                ? configuredEndpoint
                : new Uri("https://generativelanguage.googleapis.com/v1beta/");
            client.BaseAddress = endpoint.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
                ? endpoint
                : new Uri(endpoint.AbsoluteUri + '/');
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 5, 120));
        });

        return services;
    }
}
