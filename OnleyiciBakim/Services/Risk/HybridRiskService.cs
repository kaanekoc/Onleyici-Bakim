using Microsoft.Extensions.Options;
using OnleyiciBakim.Models.Ai;
using OnleyiciBakim.Options;

namespace OnleyiciBakim.Services.Risk;

public sealed class HybridRiskService(IOptions<HybridRiskOptions> options) : IHybridRiskService
{
    public HybridRiskCalculation Calculate(
        decimal ruleRiskScore,
        decimal? aiRiskScore,
        MachineFeatureDto features,
        DateTimeOffset calculatedAt)
    {
        var settings = options.Value;
        var completeness = features.DataCompletenessRate;
        var boundedRule = Math.Clamp(ruleRiskScore, 0m, 100m);
        var boundedAi = Math.Clamp(aiRiskScore ?? 0m, 0m, 100m);
        decimal aiWeight;
        decimal ruleWeight;
        if (!aiRiskScore.HasValue || completeness < settings.MinimumCompletenessThreshold)
        {
            aiWeight = 0m;
            ruleWeight = 1m;
        }
        else if (completeness >= settings.HighCompletenessThreshold)
        {
            aiWeight = settings.HighCompletenessAiWeight;
            ruleWeight = settings.HighCompletenessRuleWeight;
        }
        else
        {
            aiWeight = settings.MediumCompletenessAiWeight;
            ruleWeight = settings.MediumCompletenessRuleWeight;
        }

        var weightedScore = Math.Clamp(
            boundedAi * aiWeight + boundedRule * ruleWeight, 0m, 100m);
        var finalScore = weightedScore;
        var overrides = new List<string>();
        if (features.IsMandatoryMaintenanceOverdue)
        {
            finalScore = ElevateIntoRiskBand(weightedScore, 60m);
            overrides.Add("Zorunlu bakım tarihi geçti; nihai skor yüksek risk bandına oransal olarak yükseltildi.");
        }

        var hasCriticalOverride = features.HasCriticalTelemetry ||
                                  features.HasRepeatedCriticalFailure7Days;
        if (hasCriticalOverride)
            finalScore = ElevateIntoRiskBand(weightedScore, 80m);

        if (features.HasCriticalTelemetry)
        {
            overrides.Add("Kritik telemetri eşiği aşıldı; nihai skor kritik banda oransal olarak yükseltildi.");
        }
        if (features.HasRepeatedCriticalFailure7Days)
        {
            overrides.Add("Son 7 günde kritik arıza tekrarlandı; nihai skor kritik banda oransal olarak yükseltildi.");
        }

        return new HybridRiskCalculation(
            boundedAi,
            boundedRule,
            Math.Clamp(finalScore, 0m, 100m),
            aiWeight,
            ruleWeight,
            completeness,
            overrides,
            new Dictionary<string, decimal>
            {
                ["weightedScoreBeforeOverrides"] = weightedScore,
                ["aiContribution"] = boundedAi * aiWeight,
                ["ruleContribution"] = boundedRule * ruleWeight
            },
            calculatedAt);
    }

    private static decimal ElevateIntoRiskBand(decimal score, decimal bandMinimum)
    {
        // Sabit 60/80 ataması farklı ham skorları tek değerde topluyordu. Ham
        // skorun oranını bandın kalan kısmına taşıyarak eşik korunurken 80-100
        // aralığındaki ayrım da korunur.
        var elevated = bandMinimum + (100m - bandMinimum) * score / 100m;
        return Math.Clamp(elevated, bandMinimum, 100m);
    }
}
