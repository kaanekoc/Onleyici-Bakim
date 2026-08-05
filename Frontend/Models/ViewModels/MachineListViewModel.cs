namespace OnleyiciBakimSistemi.Models.ViewModels;

public sealed class MachineListViewModel
{
    public List<Machine> Machines { get; init; } = new();
    public List<string> Statuses { get; init; } = new();
    public List<string> RiskLevels { get; init; } = new();
    public List<string> Groups { get; init; } = new();

    public string? Search { get; init; }
    public string? SelectedStatus { get; init; }
    public string? SelectedRiskLevel { get; init; }
    public string? SelectedGroup { get; init; }
    public PaginationViewModel Pagination { get; init; } = new();
}
