using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using OnleyiciBakim.Contracts.Common;
using OnleyiciBakim.Contracts.Risk;
using OnleyiciBakim.Data;
using OnleyiciBakim.Domain.Entities;
using OnleyiciBakim.Domain.Enums;
using OnleyiciBakim.Infrastructure;
using OnleyiciBakim.Integrations.Ml;
using OnleyiciBakim.Options;

namespace OnleyiciBakim.Services;

public sealed class RiskAssessmentService(
    ApplicationDbContext dbContext,
    IAlgorithmicRiskCalculator algorithmicRiskCalculator,
    IHybridRiskCalculator hybridRiskCalculator,
    IRiskClassificationService classificationService,
    IMaintenanceRecommendationService recommendationService,
    IMlPredictionClient mlPredictionClient,
    IOptions<HybridRiskOptions> hybridOptions,
    IOptions<AlgorithmicRiskOptions> algorithmOptions,
    IOptions<IntegrationOptions> integrationOptions,
    ILogger<RiskAssessmentService> logger)
    : IRiskAssessmentService
{
    public RiskAssessmentResponse Preview(RiskPreviewRequest request)
    {
        var calculation = algorithmicRiskCalculator.Calculate(request.AlgorithmicInput);
        var prediction = new MlPredictionResult(
            true, request.MachineLearningScore, "ManualPreview", null, null,
            DateTimeOffset.UtcNow, null, [], "Provided");
        return CreateResponse(null, null, DateTimeOffset.UtcNow, calculation, prediction, null, false);
    }

    public async Task<RiskAssessmentResponse> EvaluateMachineAsync(
        string machineId,
        EvaluateMachineRiskRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.CorrelationId))
        {
            var existing = await dbContext.RiskAssessments
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.CorrelationId == request.CorrelationId, cancellationToken);
            if (existing is not null)
                return MapExisting(existing);
        }

        var machine = await dbContext.Machines
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == machineId, cancellationToken)
            ?? throw new ResourceNotFoundException($"'{machineId}' kimlikli makine bulunamadı.");

        var evaluatedAt = DateTimeOffset.UtcNow;
        var thirtyDaysAgo = evaluatedAt.AddDays(-30);
        var recentFailures = await dbContext.FailureRecords
            .AsNoTracking()
            .Where(x => x.MachineId == machineId && x.OccurredAt >= thirtyDaysAgo &&
                        x.OccurredAt <= evaluatedAt)
            .Select(x => new { x.DowntimeMinutes, x.IsRecurring, x.Severity })
            .ToListAsync(cancellationToken);
        var telemetryStart = DateOnly.FromDateTime(evaluatedAt.AddDays(-7).UtcDateTime);
        var telemetry = await dbContext.DailyTelemetry
            .AsNoTracking()
            .Where(x => x.MachineId == machineId && x.Date >= telemetryStart)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Anomaly = group.Average(x => x.AnomalyScore),
                Production = group.Average(x => x.ProductionIntensityPercent)
            })
            .FirstOrDefaultAsync(cancellationToken);

        var counterOverrun = machine.WorkingHours.HasValue && machine.CounterLimitHours is > 0m
            ? Math.Max(0m, (machine.WorkingHours.Value - machine.CounterLimitHours.Value) /
                          machine.CounterLimitHours.Value * 100m)
            : (decimal?)null;
        var input = new AlgorithmicRiskInput
        {
            DaysSinceLastMaintenance = machine.LastMaintenanceAt.HasValue
                ? Math.Max(0, (int)(evaluatedAt - machine.LastMaintenanceAt.Value).TotalDays)
                : null,
            FailureCount30Days = recentFailures.Count,
            TotalDowntimeHours30Days = recentFailures.Sum(x => x.DowntimeMinutes ?? 0) / 60m,
            RepeatedFailure = recentFailures.Any(x => x.IsRecurring),
            Criticality = machine.Criticality,
            CounterLimitOverrunPercent = counterOverrun,
            ProductionStoppingFailureCount30Days = recentFailures.Count(x => IsHighSeverity(x.Severity)),
            AnomalyScore7Days = telemetry?.Anomaly,
            ProductionIntensity7Days = telemetry?.Production
        };
        var calculation = algorithmicRiskCalculator.Calculate(input);

        MlPredictionResult prediction;
        if (request.MachineLearningScore.HasValue)
        {
            prediction = new MlPredictionResult(
                true, request.MachineLearningScore, request.ModelName, request.ModelVersion,
                request.CorrelationId, evaluatedAt, request.ModelConfidence,
                request.MachineLearningReasons ?? [], "Provided");
        }
        else
        {
            prediction = await mlPredictionClient.PredictAsync(
                new MlPredictionRequest(machineId, evaluatedAt, BuildFeatures(input), request.MockScenario),
                cancellationToken);
        }

        var isFallback = false;
        if (!prediction.IsSuccess)
        {
            logger.LogWarning(
                "ML analizi başarısız. MachineId={MachineId} ErrorCode={ErrorCode} Policy={Policy}",
                machineId, prediction.ErrorCode, hybridOptions.Value.MlFailurePolicy);
            if (hybridOptions.Value.MlFailurePolicy == MlFailurePolicy.FailClosed)
                throw new ExternalServiceException(
                    $"ML analizi tamamlanamadı ({prediction.ErrorCode}); otomatik bakım kararı verilmedi.");
            isFallback = true;
        }

        var response = CreateResponse(
            null, machineId, evaluatedAt, calculation, prediction, request.CorrelationId, isFallback);

        IDbContextTransaction? transaction = null;
        if (dbContext.Database.IsRelational())
            transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var assessment = new RiskAssessment
            {
                Id = NewId("RSK"),
                MachineId = machineId,
                EvaluatedAt = evaluatedAt,
                AlgorithmicScore = response.AlgorithmicScore,
                MachineLearningScore = response.MachineLearningScore,
                HybridScore = response.HybridRisk.Score,
                AlgorithmWeight = response.AlgorithmWeight,
                MachineLearningWeight = response.MachineLearningWeight,
                RiskLevel = classificationService.Classify(response.HybridRisk.Score),
                RiskReasons = string.Join("; ", response.Reasons),
                RecommendedAction = response.RecommendedAction,
                AlgorithmDetailsJson = JsonSerializer.Serialize(new CalculationSnapshot(
                    response.Breakdown, response.Factors, response.MissingFactors)),
                ModelName = response.ModelName ?? integrationOptions.Value.MachineLearningModelName,
                ModelVersion = response.ModelVersion,
                ModelConfidence = response.ModelConfidence,
                CorrelationId = response.CorrelationId,
                DataCompletenessPercent = response.DataCompletenessPercent,
                IsFallback = response.IsFallback,
                MlStatus = response.MlStatus,
                RuleSetVersion = algorithmOptions.Value.RuleSetVersion,
                DataSource = response.IsFallback ? "AlgorithmOnlyFallback" : "Algoritma %30 + ML %70"
            };
            dbContext.RiskAssessments.Add(assessment);
            dbContext.RiskFactorResults.AddRange(response.Factors.Select(factor =>
                new RiskFactorResultEntity
                {
                    RiskAssessmentId = assessment.Id,
                    FactorKey = factor.Key,
                    RawValue = factor.RawValue,
                    NormalizedValue = factor.NormalizedValue,
                    Weight = factor.Weight,
                    Contribution = factor.Contribution,
                    IsAvailable = factor.IsAvailable,
                    Description = factor.Description
                }));
            dbContext.MlPredictionRecords.Add(new MlPredictionRecord
            {
                Id = NewId("MLP"),
                RiskAssessmentId = assessment.Id,
                PredictionId = prediction.PredictionId,
                RiskScore = prediction.RiskScore,
                Status = prediction.Status,
                ModelName = prediction.ModelName,
                ModelVersion = prediction.ModelVersion,
                Confidence = prediction.Confidence,
                PredictedAtUtc = prediction.PredictedAtUtc,
                WarningsJson = JsonSerializer.Serialize(prediction.Warnings),
                ErrorCode = prediction.ErrorCode
            });
            dbContext.DecisionHistory.Add(new DecisionHistory
            {
                Id = NewId("DEC"),
                MachineId = machineId,
                RiskAssessmentId = assessment.Id,
                DecisionType = isFallback
                    ? DecisionType.AlgorithmOnlyFallback
                    : DecisionType.HybridAnalysis,
                FinalScore = assessment.HybridScore,
                Reason = string.Join("; ", response.Reasons),
                DecidedAtUtc = evaluatedAt,
                ModelVersion = assessment.ModelVersion,
                RuleSetVersion = assessment.RuleSetVersion
            });

            var trackedMachine = await dbContext.Machines.FirstAsync(
                x => x.Id == machineId, cancellationToken);
            trackedMachine.CurrentRiskScore = assessment.HybridScore;
            trackedMachine.CurrentRiskLevel = assessment.RiskLevel;

            if (assessment.RiskLevel is RiskLevel.High or RiskLevel.Critical)
                await CreateWarningAndOptionalDraftAsync(trackedMachine, assessment, cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return response with { Id = assessment.Id, ModelName = assessment.ModelName };
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
        }
    }

    private async Task CreateWarningAndOptionalDraftAsync(
        Machine machine,
        RiskAssessment assessment,
        CancellationToken cancellationToken)
    {
        var now = assessment.EvaluatedAt;
        var recommendation = recommendationService.Get(assessment.RiskLevel);
        var dueAt = now.AddDays(recommendation.RecommendedDays);
        if (!await dbContext.Alerts.AnyAsync(
                x => x.RiskAssessmentId == assessment.Id, cancellationToken))
        {
            dbContext.Alerts.Add(new Alert
            {
                Id = NewId("UYR"),
                GeneratedAt = now,
                MachineId = machine.Id,
                RiskAssessmentId = assessment.Id,
                Level = assessment.RiskLevel,
                Title = $"{machine.Name} için {classificationService.GetLabel(assessment.RiskLevel).ToLowerInvariant()} risk uyarısı",
                Description = assessment.RiskReasons,
                RecommendedAction = assessment.RecommendedAction,
                DueAt = dueAt
            });
        }

        var mayCreatePlan = hybridOptions.Value.AutoCreateMaintenanceDraft &&
                            !assessment.IsFallback &&
                            assessment.DataCompletenessPercent >=
                            hybridOptions.Value.MinimumDataCompletenessPercent;
        if (!mayCreatePlan)
            return;

        var activePlan = await dbContext.MaintenancePlans.AnyAsync(x =>
            x.MachineId == machine.Id &&
            x.Status != MaintenancePlanStatus.Completed &&
            x.Status != MaintenancePlanStatus.Cancelled, cancellationToken);
        if (activePlan)
            return;

        dbContext.MaintenancePlans.Add(new MaintenancePlan
        {
            Id = NewId("PLN"),
            MachineId = machine.Id,
            PlannedAt = dueAt,
            MaintenanceType = recommendation.MaintenanceType,
            Priority = classificationService.GetLabel(assessment.RiskLevel),
            Status = MaintenancePlanStatus.Suggested,
            Justification = assessment.RiskReasons,
            Recommendation = assessment.RecommendedAction,
            ModelConfidence = assessment.ModelConfidence,
            RiskAssessmentId = assessment.Id,
            DataSource = "Hibrit risk motoru"
        });
    }

    private RiskAssessmentResponse CreateResponse(
        string? id,
        string? machineId,
        DateTimeOffset evaluatedAt,
        AlgorithmicCalculationResult calculation,
        MlPredictionResult prediction,
        string? correlationId,
        bool isFallback)
    {
        var settings = hybridOptions.Value;
        var hybridScore = isFallback
            ? calculation.Score
            : hybridRiskCalculator.Calculate(
                calculation.Score,
                prediction.RiskScore ?? throw new ExternalServiceException("ML skoru bulunamadı."));
        var level = classificationService.Classify(hybridScore);
        var reasons = calculation.Reasons
            .Concat(prediction.Warnings.Where(x => !string.IsNullOrWhiteSpace(x)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new RiskAssessmentResponse(
            id, machineId, evaluatedAt, calculation.Score, prediction.RiskScore,
            isFallback ? 1m : settings.AlgorithmWeight,
            isFallback ? 0m : settings.MachineLearningWeight,
            ToPresentation(hybridScore, level), calculation.Breakdown, reasons,
            recommendationService.Get(level).Action, prediction.ModelName,
            prediction.ModelVersion, prediction.Confidence, correlationId,
            calculation.DataCompletenessPercent, calculation.MissingFactors,
            calculation.Factors, isFallback, prediction.Status);
    }

    private RiskAssessmentResponse MapExisting(RiskAssessment assessment)
    {
        var snapshot = string.IsNullOrWhiteSpace(assessment.AlgorithmDetailsJson)
            ? null
            : JsonSerializer.Deserialize<CalculationSnapshot>(assessment.AlgorithmDetailsJson);
        var breakdown = snapshot?.Breakdown ??
            new AlgorithmicScoreBreakdown(0, 0, 0, 0, 0, 0, 0, assessment.AlgorithmicScore);
        return new RiskAssessmentResponse(
            assessment.Id, assessment.MachineId, assessment.EvaluatedAt,
            assessment.AlgorithmicScore, assessment.MachineLearningScore,
            assessment.AlgorithmWeight, assessment.MachineLearningWeight,
            ToPresentation(assessment.HybridScore, assessment.RiskLevel), breakdown,
            SplitReasons(assessment.RiskReasons), assessment.RecommendedAction,
            assessment.ModelName, assessment.ModelVersion, assessment.ModelConfidence,
            assessment.CorrelationId, assessment.DataCompletenessPercent,
            snapshot?.MissingFactors ?? [], snapshot?.Factors ?? [],
            assessment.IsFallback, assessment.MlStatus);
    }

    private static IReadOnlyDictionary<string, object?> BuildFeatures(AlgorithmicRiskInput input) =>
        new Dictionary<string, object?>
        {
            ["failureCount30Days"] = input.FailureCount30Days,
            ["daysSinceLastMaintenance"] = input.DaysSinceLastMaintenance,
            ["totalDowntimeHours30Days"] = input.TotalDowntimeHours30Days,
            ["repeatedFailure"] = input.RepeatedFailure,
            ["machineCriticality"] = input.Criticality,
            ["counterLimitOverrunPercent"] = input.CounterLimitOverrunPercent,
            ["sensorAnomalyScore"] = input.AnomalyScore7Days,
            ["productionIntensityPercent"] = input.ProductionIntensity7Days
        };

    private RiskPresentation ToPresentation(decimal score, RiskLevel level) =>
        new(score, classificationService.GetLabel(level), classificationService.GetColorName(level),
            classificationService.GetColorHex(level));

    private static string[] SplitReasons(string reasons) =>
        reasons.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static bool IsHighSeverity(string value) =>
        value.Trim().ToUpperInvariant() is "YÜKSEK" or "YUKSEK" or "HIGH" or
            "KRİTİK" or "KRITIK" or "CRITICAL";

    private static string NewId(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..28];

    private sealed record CalculationSnapshot(
        AlgorithmicScoreBreakdown Breakdown,
        IReadOnlyList<RiskFactorResult> Factors,
        IReadOnlyList<string> MissingFactors);
}
