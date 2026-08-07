using System.Globalization;
using System.Net.Http.Json;
using OnleyiciBakimSistemi.Models.ViewModels;

namespace OnleyiciBakimSistemi.Services;

public interface ILocalReportClient { Task<OperationalReportViewModel?> GetAsync(string module, CancellationToken token); }

public sealed class LocalReportClient(HttpClient http) : ILocalReportClient
{
    public async Task<OperationalReportViewModel?> GetAsync(string module, CancellationToken token)
    {
        if (module == "risk-analysis")
        {
            var items = await http.GetFromJsonAsync<List<RiskAnalysisRow>>("api/v1/bakim-yonetimi/predictive/risk-analyses?take=100", token) ?? [];
            return new()
            {
                Metrics = new Dictionary<string, string>
                {
                    ["Gösterilen Analiz"] = items.Count.ToString(),
                    ["Kritik / Yüksek"] = items.Count(x => x.RiskSeviyesi is "Kritik" or "Yüksek").ToString(),
                    ["Ortalama Nihai Risk"] = items.Count == 0 ? "0" : items.Average(x => x.NihaiPuan).ToString("0.##")
                },
                Rows = items.Select(x => Row(
                    ("Tarih", FormatDate(x.AnalizTarihi)),
                    ("Makine", x.MachineName ?? x.MakineId),
                    ("Kural", FormatNumber(x.KuralPuani)),
                    ("AI", FormatNumber(x.AiRiskSkoru)),
                    ("Nihai", x.NihaiPuan.ToString("0.##")),
                    ("Risk", x.RiskSeviyesi ?? "Tanımsız"),
                    ("Veri", x.VeriYeterliligi ?? FormatPercent(x.VeriTamlikOrani)))).ToList()
            };
        }
        if (module == "alerts")
        {
            var items = await http.GetFromJsonAsync<List<WarningRow>>("api/v1/bakim-yonetimi/predictive/warnings?take=100", token) ?? [];
            return new()
            {
                Metrics = new Dictionary<string, string>
                {
                    ["Gösterilen Uyarı"] = items.Count.ToString(),
                    ["Açık Uyarı"] = items.Count(x => x.Durum != "Kapalı").ToString(),
                    ["Kritik / Yüksek"] = items.Count(x => x.UyariSeviyesi is "Kritik" or "Yüksek").ToString()
                },
                Rows = items.Select(x => Row(
                    ("Tarih", FormatDate(x.OlusturmaTarihi)),
                    ("Makine", x.MachineName ?? x.MakineId),
                    ("Seviye", x.UyariSeviyesi ?? "Tanımsız"),
                    ("Uyarı", x.Baslik ?? "Uyarı"),
                    ("Durum", x.Durum ?? "Bilinmiyor"),
                    ("Son Tarih", FormatDate(x.SonTarih)),
                    ("Sorumlu", x.ResponsiblePersonnel ?? "Atanmadı"))).ToList()
            };
        }
        if (module == "decisions")
        {
            var items = await http.GetFromJsonAsync<List<DecisionRow>>("api/v1/bakim-yonetimi/predictive/decisions?take=100", token) ?? [];
            return new()
            {
                Metrics = new Dictionary<string, string>
                {
                    ["Gösterilen Karar"] = items.Count.ToString(),
                    ["Sistemle Uyumlu"] = items.Count(x => x.Uyumlu == true).ToString(),
                    ["Değiştirilen"] = items.Count(x => x.Uyumlu == false).ToString()
                },
                Rows = items.Select(x => Row(
                    ("Tarih", FormatDate(x.KararTarihi)),
                    ("Makine", x.MachineName ?? x.MakineId),
                    ("Sistem Önerisi", x.SistemOnerisi ?? "—"),
                    ("Kullanıcı Kararı", x.KullaniciKarari ?? "—"),
                    ("Uyum", x.Uyumlu switch { true => "Uyumlu", false => "Değiştirildi", null => "—" }),
                    ("Karar Veren", x.DecisionPersonnel ?? "Sistem"))).ToList()
            };
        }
        if (module == "failure-reporting")
        {
            var x = await http.GetFromJsonAsync<FailureReport>("api/v1/reports/failure-analysis", token) ?? throw new InvalidOperationException("Rapor alınamadı.");
            return new() { Metrics = new Dictionary<string, string> { ["Toplam Arıza"] = x.FailureCount.ToString(), ["Toplam Duruş"] = x.TotalDowntimeHours.ToString("0.##") + " saat", ["Ort. Müdahale"] = x.AverageInterventionMinutes.ToString("0.##") + " dk", ["Tahmini Maliyet"] = x.TotalEstimatedCost.ToString("N2") + " ₺" }, Rows = x.ByType.Select(y => Row(("Dağılım", "Arıza Türü"), ("Kategori", y.Category), ("Adet", y.Count.ToString()))).Concat(x.BySeverity.Select(y => Row(("Dağılım", "Önem Seviyesi"), ("Kategori", y.Category), ("Adet", y.Count.ToString())))).ToList() };
        }
        if (module == "maintenance-reporting")
        {
            var x = await http.GetFromJsonAsync<MaintenanceReport>("api/v1/reports/maintenance-performance", token) ?? throw new InvalidOperationException("Rapor alınamadı.");
            return new() { Metrics = new Dictionary<string, string> { ["Gerçekleşen"] = x.CompletedCount.ToString(), ["Planlanan"] = x.PlannedCount.ToString(), ["Tamamlama Oranı"] = "%" + x.CompletionRatePercent.ToString("0.##"), ["Geciken"] = x.OverdueCount.ToString(), ["Ort. Süre"] = x.AverageDurationMinutes.ToString("0.##") + " dk", ["Toplam Maliyet"] = x.TotalCost.ToString("N2") + " ₺" } };
        }
        if (module == "model-comparison")
        {
            var x = await http.GetFromJsonAsync<ModelReport>("api/v1/reports/model-rule-comparison", token) ?? throw new InvalidOperationException("Rapor alınamadı.");
            return new() { Metrics = new Dictionary<string, string> { ["Örnek"] = x.SampleCount.ToString(), ["Ortalama Fark"] = x.MeanAbsoluteDifference.ToString("0.##"), ["Kural Ort."] = x.AverageAlgorithmicScore.ToString("0.##"), ["AI Ort."] = x.AverageMachineLearningScore.ToString("0.##"), ["Hibrit Ort."] = x.AverageHybridScore.ToString("0.##") }, Rows = x.LargestDifferences.Select(y => (IReadOnlyDictionary<string, string>)new Dictionary<string, string> { ["Makine"] = y.MachineId, ["Tarih"] = y.EvaluatedAt.LocalDateTime.ToString("g", CultureInfo.GetCultureInfo("tr-TR")), ["Kural"] = y.AlgorithmicScore.ToString("0.##"), ["AI"] = y.MachineLearningScore.ToString("0.##"), ["Nihai"] = y.HybridScore.ToString("0.##"), ["Fark"] = y.AbsoluteDifference.ToString("0.##") }).ToList() };
        }
        return null;
    }
    private static IReadOnlyDictionary<string, string> Row(params (string Key, string Value)[] values) =>
        values.ToDictionary(x => x.Key, x => x.Value);
    private static string FormatDate(DateTime value) => value.ToString("g", CultureInfo.GetCultureInfo("tr-TR"));
    private static string FormatNumber(decimal? value) => value?.ToString("0.##") ?? "—";
    private static string FormatPercent(decimal? value) => value.HasValue ? $"%{value:0.##}" : "—";

    private sealed record RiskAnalysisRow(int AnalizId, DateTime AnalizTarihi, string MakineId, string? MachineName, decimal? KuralPuani, decimal? AiRiskSkoru, decimal NihaiPuan, string? RiskSeviyesi, string? VeriYeterliligi, decimal? VeriTamlikOrani, string? ModelVersiyonu, string? HesaplamaNedeni);
    private sealed record WarningRow(string UyariId, DateTime OlusturmaTarihi, string MakineId, string? MachineName, string? UyariSeviyesi, string? Baslik, string? OnerilenAksiyon, string? Durum, DateTime SonTarih, string? ResponsiblePersonnel);
    private sealed record DecisionRow(int KararId, DateTime KararTarihi, string MakineId, string? MachineName, string? SistemOnerisi, string? KullaniciKarari, bool? Uyumlu, string? DegisiklikGerekcesi, string? DecisionPersonnel);
    private sealed record CategoryItem(string Category, int Count);
    private sealed record FailureReport(int FailureCount, decimal TotalDowntimeHours, decimal AverageInterventionMinutes, decimal TotalEstimatedCost, List<CategoryItem> ByType, List<CategoryItem> BySeverity);
    private sealed record MaintenanceReport(int CompletedCount, int PlannedCount, decimal CompletionRatePercent, decimal TotalCost, decimal AverageDurationMinutes, int OverdueCount);
    private sealed record ModelRow(string MachineId, DateTimeOffset EvaluatedAt, decimal AlgorithmicScore, decimal MachineLearningScore, decimal HybridScore, decimal AbsoluteDifference);
    private sealed record ModelReport(int SampleCount, decimal MeanAbsoluteDifference, decimal AverageAlgorithmicScore, decimal AverageMachineLearningScore, decimal AverageHybridScore, List<ModelRow> LargestDifferences);
}
