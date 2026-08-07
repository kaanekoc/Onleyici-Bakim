using OnleyiciBakim.Contracts.Dashboard;
using OnleyiciBakim.Controllers.Api;
using OnleyiciBakim.Services;
using Xunit;

namespace OnleyiciBakim.Tests;

public sealed class ReportsControllerTests
{
    [Fact]
    public async Task MissingDateRange_IncludesCompleteOperationalHistory()
    {
        var reports = new RecordingReportService();
        var controller = new ReportsController(reports);

        await controller.GetFailureAnalysis(null, null, CancellationToken.None);

        Assert.Equal(DateTimeOffset.UnixEpoch, reports.From);
        Assert.True(reports.To > reports.From);
    }

    private sealed class RecordingReportService : IReportService
    {
        public DateTimeOffset From { get; private set; }
        public DateTimeOffset To { get; private set; }

        public Task<FailureAnalysisReport> GetFailureAnalysisAsync(
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancellationToken)
        {
            From = from;
            To = to;
            return Task.FromResult(new FailureAnalysisReport(
                from, to, 0, 0, 0, 0, [], []));
        }

        public Task<MaintenancePerformanceReport> GetMaintenancePerformanceAsync(
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancellationToken) =>
            Task.FromResult(new MaintenancePerformanceReport(
                from, to, 0, 0, 0, 0, 0, 0));

        public Task<ModelRuleComparisonReport> GetModelRuleComparisonAsync(
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ModelRuleComparisonReport(0, 0, 0, 0, 0, []));
    }
}
