using Microsoft.AspNetCore.Mvc;
using OnleyiciBakimSistemi.Services;

namespace OnleyiciBakimSistemi.Controllers.Api;

// Web API controller: Dashboard sayfasındaki grafikler bu uçlardan JSON çekiyor.
// Aynı proje içinde MVC (View döndüren) ve API (JSON döndüren) controller'ların
// bir arada nasıl kullanıldığını gösteren örnek budur.
[ApiController]
[Route("api/charts")]
public class ChartsController : ControllerBase
{
    private readonly IMaintenanceDataService _data;

    public ChartsController(IMaintenanceDataService data)
    {
        _data = data;
    }

    [HttpGet("monthly-faults")]
    public async Task<IActionResult> MonthlyFaults(CancellationToken cancellationToken) =>
        Ok(await _data.GetMonthlyFaultTrendAsync(cancellationToken));

    [HttpGet("fault-types")]
    public async Task<IActionResult> FaultTypes(CancellationToken cancellationToken) =>
        Ok(await _data.GetFaultTypeDistributionAsync(cancellationToken));
}
