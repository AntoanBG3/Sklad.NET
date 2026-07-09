using Sklad.Services;

namespace Sklad.ViewModels;

public class ValueReportViewModel
{
    public required ValueReport Value { get; init; }
    public required MovementTrend Trend { get; init; }
    public required DateOnly From { get; init; }
    public required DateOnly To { get; init; }
}
