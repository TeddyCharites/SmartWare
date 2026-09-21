using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartWare.Application.Reports;
using SmartWare.Domain.Constants;
using SmartWare.Web.Helpers;
using SmartWare.Web.ViewModels.Reports;

namespace SmartWare.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.AdvancedReports)]
[Route("bao-cao")]
public sealed class ReportsController(IReportService reportService) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        string preset = "30-days",
        DateOnly? fromDate = null,
        DateOnly? toDate = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolvePeriod(preset, fromDate, toDate, out var period, out var error))
        {
            return BadRequest(error);
        }

        var report = await reportService.GetAsync(
            new ReportQuery(period.FromDate, period.ToDate),
            cancellationToken);
        return View(new ReportIndexViewModel { Preset = period.Preset, Report = report });
    }

    [HttpGet("xuat-excel")]
    public async Task<IActionResult> Export(
        string section = "overview",
        string preset = "30-days",
        DateOnly? fromDate = null,
        DateOnly? toDate = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolvePeriod(preset, fromDate, toDate, out var period, out var error))
        {
            return BadRequest(error);
        }

        var report = await reportService.GetAsync(
            new ReportQuery(period.FromDate, period.ToDate),
            cancellationToken);
        var (headers, rows, fileSection) = BuildExport(section, report);
        var content = CsvFileBuilder.Build(headers, rows);
        return File(
            content,
            "text/csv; charset=utf-8",
            $"bao-cao-{fileSection}-{period.FromDate:yyyyMMdd}-{period.ToDate:yyyyMMdd}.csv");
    }

    [HttpGet("in-pdf")]
    public async Task<IActionResult> Print(
        string preset = "30-days",
        DateOnly? fromDate = null,
        DateOnly? toDate = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolvePeriod(preset, fromDate, toDate, out var period, out var error))
        {
            return BadRequest(error);
        }

        var report = await reportService.GetAsync(
            new ReportQuery(period.FromDate, period.ToDate),
            cancellationToken);
        return View(report);
    }

    private static (IReadOnlyList<string> Headers, IEnumerable<IReadOnlyList<object?>> Rows, string Name)
        BuildExport(string section, WarehouseReport report) => section.ToLowerInvariant() switch
        {
            "products" => (
                ["Mã sản phẩm", "Tên sản phẩm", "Danh mục", "Đơn vị", "Nhập", "Xuất", "Tồn thực tế", "Đã giữ", "Khả dụng", "Giá vốn TB", "Giá trị tồn", "Doanh thu"],
                report.Products.Select(item => (IReadOnlyList<object?>)
                [item.Sku, item.ProductName, item.CategoryName, item.UnitOfMeasure, item.ImportQuantity,
                    item.ExportQuantity, item.CurrentQuantity, item.ReservedQuantity, item.AvailableQuantity,
                    item.AverageCost, item.InventoryValue, item.Revenue]),
                "san-pham"),
            "suppliers" => (
                ["Mã NCC", "Nhà cung cấp", "Số phiếu nhập", "Số lượng nhập", "Giá trị nhập", "Lần nhập gần nhất"],
                report.Suppliers.Select(item => (IReadOnlyList<object?>)
                [item.SupplierCode, item.SupplierName, item.ReceiptCount, item.ImportQuantity, item.ImportValue,
                    item.LastReceiptAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm")]),
                "nha-cung-cap"),
            "customers" => (
                ["Mã khách", "Khách hàng", "Đơn hoàn tất", "Số lượng mua", "Doanh thu", "Đơn gần nhất"],
                report.Customers.Select(item => (IReadOnlyList<object?>)
                [item.CustomerCode, item.CustomerName, item.CompletedOrderCount, item.PurchasedQuantity,
                    item.Revenue, item.LastOrderAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm")]),
                "khach-hang"),
            "forecast" => (
                ["Mã sản phẩm", "Tên sản phẩm", "Tồn khả dụng", "Tồn tối thiểu", "Tồn tối đa", "Nhu cầu TB/ngày", "Dự báo 30 ngày", "Ngày tới mức tối thiểu", "Ngày tới hết hàng", "Đề xuất bổ sung", "Rủi ro", "MAE", "RMSE", "MAPE (%)", "Đủ lịch sử"],
                report.Forecasts.Select(item => (IReadOnlyList<object?>)
                [item.Sku, item.ProductName, item.AvailableQuantity, item.MinimumStock, item.MaximumStock,
                    Math.Round(item.AverageDailyDemand, 2), item.ForecastThirtyDays,
                    item.DaysUntilMinimum.HasValue ? Math.Round(item.DaysUntilMinimum.Value, 1) : null,
                    item.DaysUntilOutOfStock.HasValue ? Math.Round(item.DaysUntilOutOfStock.Value, 1) : null,
                    item.SuggestedReplenishment, item.RiskLevel,
                    item.Mae.HasValue ? Math.Round(item.Mae.Value, 2) : null,
                    item.Rmse.HasValue ? Math.Round(item.Rmse.Value, 2) : null,
                    item.Mape.HasValue ? Math.Round(item.Mape.Value, 2) : null,
                    item.HasHistory ? "Có" : "Không"]),
                "du-bao"),
            _ => (
                ["Ngày", "Số lượng nhập", "Số lượng xuất", "Giá trị nhập", "Giá vốn xuất"],
                report.Movements.Select(item => (IReadOnlyList<object?>)
                [item.Date.ToString("dd/MM/yyyy"), item.ImportQuantity, item.ExportQuantity,
                    item.ImportValue, item.ExportCost]),
                "tong-quan")
        };

    private static (string Preset, DateOnly FromDate, DateOnly ToDate) ResolvePeriod(
        string? preset,
        DateOnly? fromDate,
        DateOnly? toDate)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var normalized = preset?.Trim().ToLowerInvariant() ?? "30-days";
        if (normalized == "custom" && (!fromDate.HasValue || !toDate.HasValue))
        {
            normalized = "30-days";
        }

        var period = normalized switch
        {
            "7-days" => (today.AddDays(-6), today),
            "month" => (new DateOnly(today.Year, today.Month, 1), today),
            "quarter" => (new DateOnly(today.Year, ((today.Month - 1) / 3 * 3) + 1, 1), today),
            "year" => (new DateOnly(today.Year, 1, 1), today),
            "custom" when fromDate.HasValue && toDate.HasValue => (fromDate.Value, toDate.Value),
            _ => (today.AddDays(-29), today)
        };

        if (period.Item1 > period.Item2)
        {
            throw new ArgumentException("Ngày bắt đầu không được sau ngày kết thúc.");
        }

        if (period.Item2.DayNumber - period.Item1.DayNumber + 1 > 366)
        {
            throw new ArgumentException("Khoảng báo cáo không được vượt quá 366 ngày.");
        }

        return (normalized is "7-days" or "month" or "quarter" or "year" or "custom"
            ? normalized
            : "30-days", period.Item1, period.Item2);
    }

    private static bool TryResolvePeriod(
        string? preset,
        DateOnly? fromDate,
        DateOnly? toDate,
        out (string Preset, DateOnly FromDate, DateOnly ToDate) period,
        out string? error)
    {
        try
        {
            period = ResolvePeriod(preset, fromDate, toDate);
            error = null;
            return true;
        }
        catch (ArgumentException exception)
        {
            period = default;
            error = exception.Message;
            return false;
        }
    }
}
