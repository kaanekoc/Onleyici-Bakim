using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OnleyiciBakim.Options;

namespace OnleyiciBakim.Integrations.Ml;

public sealed record MlPredictionRequest(
    string MachineId,
    DateTimeOffset AnalysisTimeUtc,
    IReadOnlyDictionary<string, object?> Features,
    string? Scenario = null);

public sealed record MlPredictionResult(
    bool IsSuccess,
    decimal? RiskScore,
    string? ModelName,
    string? ModelVersion,
    string? PredictionId,
    DateTimeOffset? PredictedAtUtc,
    decimal? Confidence,
    IReadOnlyList<string> Warnings,
    string Status,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public interface IMlPredictionClient
{
    Task<MlPredictionResult> PredictAsync(MlPredictionRequest request, CancellationToken cancellationToken);
    Task<bool> IsReadyAsync(CancellationToken cancellationToken);
}

public sealed class HttpMlPredictionClient(
    HttpClient httpClient,
    IOptions<MlServiceOptions> options,
    ILogger<HttpMlPredictionClient> logger) : IMlPredictionClient
{
    private readonly MlServiceOptions _options = options.Value;

    public async Task<MlPredictionResult> PredictAsync(
        MlPredictionRequest request,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;
        for (var attempt = 0; attempt <= _options.RetryCount; attempt++)
        {
            try
            {
                using var message = new HttpRequestMessage(HttpMethod.Post, _options.PredictionEndpoint)
                {
                    Content = JsonContent.Create(request)
                };
                AddApiKey(message);
                using var response = await httpClient.SendAsync(message, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var retryable = response.StatusCode is HttpStatusCode.RequestTimeout or
                        HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError;
                    if (retryable && attempt < _options.RetryCount)
                        continue;
                    return Failed($"http-{(int)response.StatusCode}",
                        $"ML servisi HTTP {(int)response.StatusCode} döndürdü.");
                }

                var payload = await response.Content.ReadFromJsonAsync<MlApiResponse>(
                    cancellationToken: cancellationToken);
                if (payload is null)
                    return Failed("invalid-json", "ML servisi boş veya geçersiz JSON döndürdü.");
                if (payload.RiskScore is < 0m or > 100m)
                    return Failed("invalid-score", "ML risk puanı 0-100 aralığında değil.");

                return new MlPredictionResult(
                    true, payload.RiskScore, payload.ModelName, payload.ModelVersion,
                    payload.PredictionId, payload.PredictedAtUtc, payload.Confidence,
                    payload.Warnings ?? [], "Success");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lastException = new TimeoutException("ML isteği zaman aşımına uğradı.");
            }
            catch (HttpRequestException exception)
            {
                lastException = exception;
            }
            catch (JsonException exception)
            {
                return Failed("invalid-json", exception.Message);
            }

            if (attempt < _options.RetryCount)
                await Task.Delay(TimeSpan.FromMilliseconds(100 * (attempt + 1)), cancellationToken);
        }

        logger.LogWarning(lastException, "ML tahmini alınamadı. MachineId={MachineId}", request.MachineId);
        return Failed(lastException is TimeoutException ? "timeout" : "unavailable",
            "ML servisine ulaşılamadı.");
    }

    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, "/health");
            AddApiKey(message);
            using var response = await httpClient.SendAsync(message, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private void AddApiKey(HttpRequestMessage message)
    {
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            message.Headers.TryAddWithoutValidation("X-Api-Key", _options.ApiKey);
    }

    private static MlPredictionResult Failed(string code, string message) =>
        new(false, null, null, null, null, null, null, [], "Failed", code, message);

    private sealed class MlApiResponse
    {
        public decimal RiskScore { get; init; }
        public string? ModelName { get; init; }
        public string? ModelVersion { get; init; }
        public string? PredictionId { get; init; }
        public DateTimeOffset? PredictedAtUtc { get; init; }
        public decimal? Confidence { get; init; }
        public string[]? Warnings { get; init; }
    }
}

public sealed class MockMlPredictionClient : IMlPredictionClient
{
    public async Task<MlPredictionResult> PredictAsync(
        MlPredictionRequest request,
        CancellationToken cancellationToken)
    {
        switch (request.Scenario?.Trim().ToLowerInvariant())
        {
            case "timeout":
                await Task.Delay(50, cancellationToken);
                return new(false, null, null, null, null, null, null, [], "Failed",
                    "timeout", "Simüle edilmiş ML timeout.");
            case "unavailable":
                return new(false, null, null, null, null, null, null, [], "Failed",
                    "unavailable", "Simüle edilmiş ML servis kesintisi.");
            case "invalid-score":
                return new(false, null, "MockRiskModel", "dev-1", null, null, null, [], "Failed",
                    "invalid-score", "Simüle edilmiş geçersiz skor.");
        }

        var failures = DecimalFeature(request, "failureCount30Days");
        var maintenanceDays = DecimalFeature(request, "daysSinceLastMaintenance");
        var downtime = DecimalFeature(request, "totalDowntimeHours30Days");
        var anomaly = DecimalFeature(request, "sensorAnomalyScore");
        var score = Math.Clamp(
            failures * 8m + maintenanceDays / 3m + downtime * 1.5m + anomaly * 0.20m,
            0m,
            100m);
        return new(true, score, "MockRiskModel", "dev-1.0",
            $"mock-{Guid.NewGuid():N}", DateTimeOffset.UtcNow, 0.86m, [], "Success");
    }

    public Task<bool> IsReadyAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    private static decimal DecimalFeature(MlPredictionRequest request, string key)
    {
        if (!request.Features.TryGetValue(key, out var value) || value is null)
            return 0m;
        return Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture);
    }
}
