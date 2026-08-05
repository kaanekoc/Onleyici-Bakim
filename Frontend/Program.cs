using OnleyiciBakimSistemi.Services;

var builder = WebApplication.CreateBuilder(args);

// MVC (Razor View) ve Web API controller'ları aynı altyapı üzerinden ekleniyor.
builder.Services.AddControllersWithViews();

var useMockData = builder.Configuration.GetValue<bool>("Backend:UseMockData");
if (useMockData)
{
    if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException("Frontend mock verisi yalnızca Development ortamında kullanılabilir.");
    builder.Services.AddSingleton<IMaintenanceDataService, MockMaintenanceDataService>();
}
else
{
    var backendBaseUrl = builder.Configuration["Backend:BaseUrl"]
        ?? throw new InvalidOperationException("Backend:BaseUrl yapılandırması bulunamadı.");
    builder.Services.AddHttpClient<IMaintenanceDataService, BackendMaintenanceDataService>(client =>
    {
        client.BaseAddress = new Uri(backendBaseUrl);
        client.Timeout = TimeSpan.FromSeconds(30);
    });
}

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error/500");
    app.UseHsts();
}

// 404, 403, 401 gibi durum kodlarını yakalayıp özel tasarlanmış sayfalara yönlendirir.
// Örn. var olmayan bir /Machines/Details/999 -> 404 -> Views/Error/NotFound.cshtml
app.UseStatusCodePagesWithReExecute("/Error/{0}");

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
