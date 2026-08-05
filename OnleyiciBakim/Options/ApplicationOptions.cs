namespace OnleyiciBakim.Options;

public sealed class HybridRiskOptions
{
    public const string SectionName = "HybridRisk";

    public decimal AlgorithmWeight { get; set; } = 0.30m;
    public decimal MachineLearningWeight { get; set; } = 0.70m;
    public MlFailurePolicy MlFailurePolicy { get; set; } = MlFailurePolicy.FailClosed;
    public decimal MinimumDataCompletenessPercent { get; set; } = 60m;
    public bool AutoCreateMaintenanceDraft { get; set; } = true;
}

public enum MlFailurePolicy
{
    FailClosed,
    AlgorithmOnlyFallback
}

public sealed class AlgorithmicRiskOptions
{
    public const string SectionName = "AlgorithmicRisk";

    public decimal FailureFrequencyWeight { get; set; } = 0.25m;
    public decimal MaintenanceAgeWeight { get; set; } = 0.20m;
    public decimal DowntimeWeight { get; set; } = 0.15m;
    public decimal RepeatedFailureWeight { get; set; } = 0.15m;
    public decimal CriticalityWeight { get; set; } = 0.10m;
    public decimal CounterOverrunWeight { get; set; } = 0.10m;
    public decimal ProductionImpactWeight { get; set; } = 0.05m;
    public int FailureCountMaximum { get; set; } = 5;
    public int MaintenanceAgeMaximumDays { get; set; } = 180;
    public decimal DowntimeMaximumHours { get; set; } = 24m;
    public decimal CounterOverrunMaximumPercent { get; set; } = 100m;
    public int ProductionStoppingFailureMaximum { get; set; } = 3;
    public string RuleSetVersion { get; set; } = "algorithmic-v1";
}

public sealed class MlServiceOptions
{
    public const string SectionName = "MlService";

    public string BaseUrl { get; set; } = "https://ml-service.example";
    public string PredictionEndpoint { get; set; } = "/api/predict";
    public int TimeoutSeconds { get; set; } = 15;
    public string ApiKey { get; set; } = string.Empty;
    public int RetryCount { get; set; } = 2;
    public bool UseMock { get; set; }
}

public sealed class ErpOptions
{
    public const string SectionName = "Erp";

    public string Provider { get; set; } = "Generic";
    public string BaseUrl { get; set; } = "https://erp.example";
    public int TimeoutSeconds { get; set; } = 30;
    public string ApiKey { get; set; } = string.Empty;
    public int RetryCount { get; set; } = 2;
    public bool UseMock { get; set; }
    public bool EnableBackgroundSync { get; set; }
    public int OutboxPollingSeconds { get; set; } = 30;
}

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string Provider { get; set; } = "PostgreSql";
    public bool SeedDevelopmentData { get; set; }
    public bool UseBakimYonetimiReadModel { get; set; }
}

public sealed class SwaggerOptions
{
    public const string SectionName = "Swagger";
    public bool Enabled { get; set; }
}

public sealed class MaintenanceRecommendationOptions
{
    public const string SectionName = "MaintenanceRecommendations";
    public int LowDays { get; set; } = 60;
    public int MediumDays { get; set; } = 30;
    public int HighDays { get; set; } = 15;
    public int CriticalDays { get; set; } = 7;
    public string LowType { get; set; } = "Periyodik";
    public string MediumType { get; set; } = "Planlı";
    public string HighType { get; set; } = "Acil";
    public string CriticalType { get; set; } = "Acil müdahale";
}

public sealed class IntegrationOptions
{
    public const string SectionName = "Integrations";

    public string ApiKey { get; set; } = string.Empty;
    public string MachineLearningModelName { get; set; } = "predictive-maintenance";
    public string ErpSystemName { get; set; } = "ERP";
}
