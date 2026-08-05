using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnleyiciBakim.Contracts.Risk;
using OnleyiciBakim.Data;
using OnleyiciBakim.Options;
using OnleyiciBakim.Services;

namespace OnleyiciBakim.BackgroundJobs;

public sealed class RiskRecalculationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<RiskAutomationOptions> options,
    ILogger<RiskRecalculationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("Otomatik risk yeniden hesaplama görevi devre dışı.");
            return;
        }

        await RunOnceAsync(settings.BatchSize, stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromHours(settings.IntervalHours));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await RunOnceAsync(settings.BatchSize, stoppingToken);
    }

    private async Task RunOnceAsync(int batchSize, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var riskService = scope.ServiceProvider.GetRequiredService<IRiskAssessmentService>();
            var machineIds = await dbContext.Machines.AsNoTracking()
                .Where(x => x.Status == "Aktif")
                .OrderBy(x => x.Id)
                .Select(x => x.Id)
                .Take(batchSize)
                .ToListAsync(cancellationToken);
            var runDate = DateTimeOffset.UtcNow.ToString("yyyyMMdd");
            foreach (var machineId in machineIds)
            {
                try
                {
                    await riskService.EvaluateMachineAsync(
                        machineId,
                        new EvaluateMachineRiskRequest
                        {
                            CorrelationId = $"auto-{runDate}-{machineId}"
                        },
                        cancellationToken);
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                {
                    logger.LogError(exception,
                        "Otomatik risk hesaplama başarısız. MachineId={MachineId}", machineId);
                }
            }
            logger.LogInformation("Otomatik risk hesaplama tamamlandı. MachineCount={MachineCount}",
                machineIds.Count);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Otomatik risk hesaplama turu başlatılamadı.");
        }
    }
}
