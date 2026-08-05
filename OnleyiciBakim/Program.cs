using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi.Models;
using OnleyiciBakim.Data;
using OnleyiciBakim.Data.BakimYonetimi;
using OnleyiciBakim.Infrastructure;
using OnleyiciBakim.Integrations.Erp;
using OnleyiciBakim.Integrations.Ml;
using OnleyiciBakim.Options;
using OnleyiciBakim.Services;
using OnleyiciBakim.BackgroundJobs;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllersWithViews()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.AddProblemDetails();
builder.Services.AddHttpContextAccessor();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Önleyici Bakım API",
        Version = "v1",
        Description = "ERP, kestirimci bakım ML modeli ve frontend arasında çalışan hibrit bakım backend servisi."
    });
    var xmlPath = Path.Combine(AppContext.BaseDirectory, $"{typeof(Program).Assembly.GetName().Name}.xml");
    if (File.Exists(xmlPath))
        options.IncludeXmlComments(xmlPath);
});

builder.Services.AddOptions<HybridRiskOptions>()
    .Bind(builder.Configuration.GetSection(HybridRiskOptions.SectionName))
    .Validate(x => x.AlgorithmWeight >= 0m && x.MachineLearningWeight >= 0m &&
                   x.AlgorithmWeight + x.MachineLearningWeight == 1m,
        "Hibrit risk ağırlıklarının toplamı 1.00 olmalıdır.")
    .Validate(x => x.MinimumDataCompletenessPercent is >= 0m and <= 100m,
        "Minimum veri yeterliliği 0-100 aralığında olmalıdır.")
    .ValidateOnStart();
builder.Services.AddOptions<AlgorithmicRiskOptions>()
    .Bind(builder.Configuration.GetSection(AlgorithmicRiskOptions.SectionName))
    .Validate(x => x.FailureFrequencyWeight + x.MaintenanceAgeWeight + x.DowntimeWeight +
                   x.RepeatedFailureWeight + x.CriticalityWeight + x.CounterOverrunWeight +
                   x.ProductionImpactWeight == 1m,
        "Algoritmik risk faktörü ağırlıklarının toplamı 1.00 olmalıdır.")
    .ValidateOnStart();
builder.Services.AddOptions<MlServiceOptions>()
    .Bind(builder.Configuration.GetSection(MlServiceOptions.SectionName))
    .Validate(x => x.TimeoutSeconds is > 0 and <= 300, "ML timeout 1-300 saniye olmalıdır.")
    .ValidateOnStart();
builder.Services.AddOptions<ErpOptions>()
    .Bind(builder.Configuration.GetSection(ErpOptions.SectionName))
    .Validate(x => x.TimeoutSeconds is > 0 and <= 300, "ERP timeout 1-300 saniye olmalıdır.")
    .ValidateOnStart();
builder.Services.Configure<DatabaseOptions>(
    builder.Configuration.GetSection(DatabaseOptions.SectionName));
builder.Services.Configure<IntegrationOptions>(
    builder.Configuration.GetSection(IntegrationOptions.SectionName));
builder.Services.Configure<MaintenanceRecommendationOptions>(
    builder.Configuration.GetSection(MaintenanceRecommendationOptions.SectionName));

var databaseProvider = builder.Configuration[$"{DatabaseOptions.SectionName}:Provider"] ?? "PostgreSql";
if (databaseProvider.Equals("InMemory", StringComparison.OrdinalIgnoreCase))
{
    if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Test"))
        throw new InvalidOperationException("InMemory veritabanı Production ortamında kullanılamaz.");
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseInMemoryDatabase("onleyici_bakim_development"));
}

var useBakimYonetimiReadModel = builder.Configuration.GetValue<bool>(
    $"{DatabaseOptions.SectionName}:UseBakimYonetimiReadModel");
if (useBakimYonetimiReadModel)
{
    var bakimYonetimiConnection = builder.Configuration.GetConnectionString("BakimYonetimiSqlServer")
        ?? throw new InvalidOperationException(
            "ConnectionStrings:BakimYonetimiSqlServer yapılandırması bulunamadı.");
    builder.Services.AddDbContext<BakimYonetimiDbContext>(options =>
        options.UseSqlServer(bakimYonetimiConnection, sqlServer =>
            sqlServer.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null)));
}
else
{
    var connectionString = builder.Configuration.GetConnectionString("PostgreSql")
        ?? throw new InvalidOperationException("ConnectionStrings:PostgreSql yapılandırması bulunamadı.");
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseNpgsql(connectionString, npgsql =>
            npgsql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null)));
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? ["http://localhost:3000", "http://localhost:5173", "https://localhost:7080", "http://localhost:5080"];
        policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
    });
});

var mlSettings = builder.Configuration.GetSection(MlServiceOptions.SectionName).Get<MlServiceOptions>()
    ?? new MlServiceOptions();
var erpSettings = builder.Configuration.GetSection(ErpOptions.SectionName).Get<ErpOptions>()
    ?? new ErpOptions();
if ((mlSettings.UseMock || erpSettings.UseMock) &&
    !builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Test"))
    throw new InvalidOperationException("Mock ML/ERP istemcileri yalnızca Development/Test ortamında kullanılabilir.");

if (mlSettings.UseMock)
{
    builder.Services.AddSingleton<IMlPredictionClient, MockMlPredictionClient>();
}
else
{
    builder.Services.AddHttpClient<IMlPredictionClient, HttpMlPredictionClient>(client =>
    {
        client.BaseAddress = new Uri(mlSettings.BaseUrl);
        client.Timeout = TimeSpan.FromSeconds(mlSettings.TimeoutSeconds);
    });
}

if (erpSettings.UseMock)
{
    builder.Services.AddSingleton<IErpClient, MockErpClient>();
}
else
{
    builder.Services.AddHttpClient<IErpClient, HttpErpClient>(client =>
    {
        client.BaseAddress = new Uri(erpSettings.BaseUrl);
        client.Timeout = TimeSpan.FromSeconds(erpSettings.TimeoutSeconds);
    });
}

var healthChecks = builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddCheck<DatabaseReadinessHealthCheck>("postgresql", tags: ["ready"])
    .AddCheck<MlReadinessHealthCheck>("ml", tags: ["ready"])
    .AddCheck<ErpReadinessHealthCheck>("erp", tags: ["ready"]);
if (useBakimYonetimiReadModel)
    healthChecks.AddCheck<BakimYonetimiDatabaseReadinessHealthCheck>(
        "bakim-yonetimi-sqlserver", tags: ["ready"]);
builder.Services.AddScoped<IRiskClassificationService, RiskClassificationService>();
builder.Services.AddScoped<IMaintenanceRecommendationService, MaintenanceRecommendationService>();
builder.Services.AddScoped<IAlgorithmicRiskCalculator, AlgorithmicRiskCalculator>();
builder.Services.AddScoped<IHybridRiskCalculator, HybridRiskCalculator>();
builder.Services.AddScoped<IRiskAssessmentService, RiskAssessmentService>();
if (useBakimYonetimiReadModel)
    builder.Services.AddScoped<IDashboardService, BakimYonetimiDashboardService>();
else
    builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IIntegrationService, IntegrationService>();
builder.Services.AddScoped<IDevTestService, DevTestService>();
builder.Services.AddSingleton<IWorkflowTransitionService, WorkflowTransitionService>();
builder.Services.AddSingleton<ICsvImportService, CsvImportService>();
builder.Services.AddScoped<IntegrationApiKeyFilter>();
builder.Services.AddScoped<DevelopmentDataSeeder>();
builder.Services.AddHostedService<ErpOutboxWorker>();

var app = builder.Build();

app.UseMiddleware<ApiExceptionMiddleware>();
app.Use(async (context, next) =>
{
    var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault()
        ?? context.TraceIdentifier;
    context.TraceIdentifier = correlationId;
    context.Response.Headers["X-Correlation-ID"] = correlationId;
    await next();
});

var swaggerEnabled = app.Environment.IsDevelopment() ||
                     builder.Configuration.GetValue<bool>($"{SwaggerOptions.SectionName}:Enabled");
if (swaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Önleyici Bakım API v1");
        options.RoutePrefix = "swagger";
    });
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseCors("Frontend");
app.UseRouting();
app.UseAuthorization();

app.MapControllers();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

static Task WriteHealth(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";
    var payload = new
    {
        status = report.Status.ToString(),
        checks = report.Entries.Select(x => new
        {
            name = x.Key,
            status = x.Value.Status.ToString(),
            description = x.Value.Description
        }),
        durationMs = report.TotalDuration.TotalMilliseconds
    };
    return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
}

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("live"),
    ResponseWriter = WriteHealth
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = WriteHealth
});
app.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = WriteHealth });

if (app.Environment.IsDevelopment() &&
    builder.Configuration.GetValue<bool>($"{DatabaseOptions.SectionName}:SeedDevelopmentData"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<DevelopmentDataSeeder>().SeedAsync();
}

app.Run();

public partial class Program;
