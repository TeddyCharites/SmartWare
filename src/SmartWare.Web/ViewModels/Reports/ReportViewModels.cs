using SmartWare.Application.Reports;

namespace SmartWare.Web.ViewModels.Reports;

public sealed class ReportIndexViewModel
{
    public required string Preset { get; init; }
    public required WarehouseReport Report { get; init; }
}
