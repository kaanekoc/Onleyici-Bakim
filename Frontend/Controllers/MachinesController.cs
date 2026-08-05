using Microsoft.AspNetCore.Mvc;
using OnleyiciBakimSistemi.Models.ViewModels;
using OnleyiciBakimSistemi.Services;

namespace OnleyiciBakimSistemi.Controllers;

public class MachinesController : Controller
{
    private readonly IMaintenanceDataService _data;

    public MachinesController(IMaintenanceDataService data)
    {
        _data = data;
    }

    public async Task<IActionResult> Index(
        string? search,
        string? status,
        string? riskLevel,
        string? group,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        var allMachines = (await _data.GetMachinesAsync(cancellationToken)).ToList();
        var machines = allMachines.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            machines = machines.Where(m =>
                m.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                m.Code.Contains(term, StringComparison.CurrentCultureIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(status))
            machines = machines.Where(m => string.Equals(m.Status, status, StringComparison.CurrentCultureIgnoreCase));
        if (!string.IsNullOrWhiteSpace(riskLevel))
            machines = machines.Where(m => string.Equals(m.RiskLevel, riskLevel, StringComparison.CurrentCultureIgnoreCase));
        if (!string.IsNullOrWhiteSpace(group))
            machines = machines.Where(m => string.Equals(m.Type, group, StringComparison.CurrentCultureIgnoreCase));

        var filteredMachines = machines.OrderByDescending(m => m.RiskScore).ToList();
        var totalPages = Math.Max(1, (int)Math.Ceiling(
            filteredMachines.Count / (double)PaginationViewModel.DefaultPageSize));
        page = Math.Clamp(page, 1, totalPages);

        var routeValues = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(search)) routeValues["search"] = search;
        if (!string.IsNullOrWhiteSpace(status)) routeValues["status"] = status;
        if (!string.IsNullOrWhiteSpace(riskLevel)) routeValues["riskLevel"] = riskLevel;
        if (!string.IsNullOrWhiteSpace(group)) routeValues["group"] = group;

        return View(new MachineListViewModel
        {
            Machines = filteredMachines
                .Skip((page - 1) * PaginationViewModel.DefaultPageSize)
                .Take(PaginationViewModel.DefaultPageSize)
                .ToList(),
            Statuses = allMachines.Select(m => m.Status).Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(x => x).ToList(),
            RiskLevels = allMachines.Select(m => m.RiskLevel).Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(x => x).ToList(),
            Groups = allMachines.Select(m => m.Type).Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(x => x).ToList(),
            Search = search,
            SelectedStatus = status,
            SelectedRiskLevel = riskLevel,
            SelectedGroup = group,
            Pagination = new PaginationViewModel
            {
                Controller = "Machines",
                Page = page,
                TotalCount = filteredMachines.Count,
                RouteValues = routeValues
            }
        });
    }

    // id yoksa/olmayan bir id gelirse 404'e düşer (Program.cs'teki
    // UseStatusCodePagesWithReExecute bunu Views/Error/NotFound.cshtml'e yönlendirir).
    public async Task<IActionResult> Details(
        string id,
        int faultPage = 1,
        CancellationToken cancellationToken = default)
    {
        var machine = await _data.GetMachineAsync(id, cancellationToken);
        if (machine is null)
        {
            return NotFound();
        }

        var allFaults = (await _data.GetFaultsByMachineAsync(id, cancellationToken))
            .OrderByDescending(f => f.Date)
            .ToList();
        var totalPages = Math.Max(1, (int)Math.Ceiling(
            allFaults.Count / (double)PaginationViewModel.DefaultPageSize));
        faultPage = Math.Clamp(faultPage, 1, totalPages);

        var vm = new MachineDetailViewModel
        {
            Machine = machine,
            Faults = allFaults
                .Skip((faultPage - 1) * PaginationViewModel.DefaultPageSize)
                .Take(PaginationViewModel.DefaultPageSize)
                .ToList(),
            FaultPagination = new PaginationViewModel
            {
                Controller = "Machines",
                Action = "Details",
                Page = faultPage,
                TotalCount = allFaults.Count,
                RouteValues = new Dictionary<string, string> { ["id"] = id }
            }
        };

        return View(vm);
    }
}
