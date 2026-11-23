# Önleyici Bakım Sistemi

ASP.NET Core 8 tabanlı çözüm; merkezi `BakimYonetimiDb` SQL Server şemasını normal kullanıcı
akışlarında hem okuma hem yazma modeli olarak kullanır. ERP kapalıdır ve menüde gösterilmez;
mevcut ERP kolonları gelecekte kullanım için korunmuştur. Python ML servisi veritabanına
bağlanmaz, yalnızca backend'in ürettiği 20 özellik üzerinden tahmin verir.

## Projeler

- `OnleyiciBakim`: REST API, SQL Server ekran modeli, PostgreSQL komut modeli, ML/ERP adapter'ları ve Development
  test paneli.
- `Frontend`: backend API'sini kullanan MVC frontend.
- `OnleyiciBakim.Tests`: unit ve API integration testleri.
- `MlService`: Python 3.11, Random Forest tabanlı bağımsız AI risk servisi ve eğitim/test araçları.

Detaylı backend teslim dokümanı: [BACKEND_README.md](BACKEND_README.md)  
Frontend sözleşmesi: [FRONTEND_INTEGRATION_GUIDE.md](FRONTEND_INTEGRATION_GUIDE.md)  
HTTP örnekleri: [OnleyiciBakim.http](OnleyiciBakim.http)

## Hızlı başlangıç

Gereksinimler: Windows, .NET 8 SDK veya daha yenisi, Python 3.11, SQL Server LocalDB,
`sqlcmd` ve Git. `BakimYonetimiDb_before_20260805.bak` dosyası kişisel/veritabanı verisi
içerdiği için depoya eklenmez; ilk kurulumdan önce bu yedeği `Downloads` klasörüne koyun.
Sonra depoyu indirip geliştirme sürümünü başlatın:

```powershell
git clone https://github.com/kaanekoc/Onleyici-Bakim.git; Set-Location .\Onleyici-Bakim; .\scripts\Start-Local.ps1
```

Betik ilk açılışta veritabanını yedekten kurar ve tamamlama SQL'ini uygular; sonraki
çalıştırmalarda mevcut veritabanını kullanır. Python ML paketlerini kurar ve backend,
ML servisi ile web arayüzünü ayrı pencerelerde başlatır. Arayüz `http://localhost:5080`,
Swagger `http://localhost:5224/swagger` adresindedir. Pencereleri kapatarak servisleri
durdurabilirsiniz. Yedek başka konumdaysa `Start-Local.ps1 -BackupPath "C:\yedek\BakimYonetimiDb.bak"`
şeklinde çalıştırın.

Gerçek `BakimYonetimiDb` verisiyle çalışmak için önce aşağıdaki SQL Server kurulumunu tamamlayın.

### Gerçek SQL Server verisiyle çalıştırma

SQL Server LocalDB ve `sqlcmd` kurulu olmalıdır. `BakimYonetimiDb_before_20260805.bak`
yedeğini GitHub deposundan ayrı olarak `Downloads` klasörüne koyun, ardından depo kökünde:

```powershell
.\scripts\Restore-BakimYonetimiDb.ps1 -BackupPath "$env:USERPROFILE\Downloads\BakimYonetimiDb_before_20260805.bak"
sqlcmd -S "(localdb)\MSSQLLocalDB" -d BakimYonetimiDb -E -b -f 65001 -i .\scripts\20260805_PreventiveMaintenanceCompletion.sql
```

Türkçe metin onarımı yalnızca tamamlama betiği daha önce `-f 65001` olmadan çalıştırıldıysa gerekir:

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -d BakimYonetimiDb -E -b -f 65001 -i .\scripts\20260806_RepairTurkishText.sql
```

Ardından `scripts\Start-Local.ps1` betiği mevcut veritabanını algılar ve uygulamayı başlatır.

ML ayrıntıları ve güncel model metrikleri: [MlService/README.md](MlService/README.md)

Backend:

- API: `http://localhost:5224/api/v1`
- Swagger: `http://localhost:5224/swagger`
- Development test paneli: `http://localhost:5224/dev-test`
- Health: `http://localhost:5224/health`, `/health/live`, `/health/ready`

Frontend: `http://localhost:5080`

## Veritabanı çalışma biçimi

- `BakimYonetimiDbContext`, 44 iş tablosunu ve ilişkilerini Database First + güvenli manuel
  SQL yaklaşımıyla eşler.
- Dashboard, tanımlar, makineler, sayaçlar, arıza/bakım/uyarı/iş emri akışları ve raporlar
  gerçek SQL Server verisini okur ve yazar.
- Kaynak şema uygulama başlangıcında değiştirilmez; migration veya `EnsureCreated` çalıştırılmaz.
- Yeni yerel iş kayıtlarında `VeriKaynagi = "LOCAL"` kullanılır. Eski PostgreSQL komut modeli
  uyumluluk amacıyla kodda tutulur ancak MVC'nin normal bakım yönetimi akışında kullanılmaz.
- Uygulama başlangıçta destructive veritabanı işlemi çalıştırmaz.

## Kesin risk kuralı

- `[0,30)`: Düşük / `green`
- `[30,60)`: Orta / `amber`
- `[60,80)`: Yüksek / `red`
- `[80,100]`: Kritik / `dark-red`

Hibrit skor veri tamlığına göre ara yuvarlama yapılmadan hesaplanır:

- Tamlık `%80+`: `AI × 0.40 + kural × 0.60`
- Tamlık `%50–79`: `AI × 0.20 + kural × 0.80`
- Tamlık `%50 altı` veya ML kesintisi: yalnızca kural skoru

Kritik telemetri ve son 7 gündeki tekrarlayan kritik arıza skoru en az 80'e;
geçmiş zorunlu bakım tarihi en az 60'a yükseltir. Kesin bakım tarihi AI cevabından
değil, nihai hibrit seviyeden (`60/21/7/3` gün) backend tarafından oluşturulur.
