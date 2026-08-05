# Önleyici Bakım Sistemi

ASP.NET Core 8 tabanlı çözüm; merkezi `BakimYonetimiDb` SQL Server şemasını salt-okunur ekran
modeli olarak, mevcut PostgreSQL/InMemory iş akışı altyapısını da komut modeli olarak kullanır.
ERP ve ML tahmin servisi adaptörleri ile MVC frontend aynı çözüm içindedir.

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

İlk kurulumda gönderilen en güncel yedeği LocalDB'ye geri yükleyin:

```powershell
.\scripts\Restore-BakimYonetimiDb.ps1
```

Betik mevcut bir `BakimYonetimiDb` üzerine yazmaz. Bilinçli yenileme gerekiyorsa `-Replace`
kullanılabilir. Ardından Development profili SQL Server ekran modelini, gerçek yerel ML HTTP
servisini ve Development-only ERP mock'unu birlikte kullanır:

```powershell
dotnet restore .\OnleyiciBakim.sln
dotnet build .\OnleyiciBakim.sln
dotnet test .\OnleyiciBakim.sln
dotnet run --project .\OnleyiciBakim\OnleyiciBakim.csproj
```

ML servisini ayrı terminalde başlatın:

```powershell
cd .\MlService
py -3.11 -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements-dev.txt
.\.venv\Scripts\python.exe .\app.py
```

ML ayrıntıları ve güncel model metrikleri: [MlService/README.md](MlService/README.md)

Backend:

- API: `http://localhost:5224/api/v1`
- Swagger: `http://localhost:5224/swagger`
- Development test paneli: `http://localhost:5224/dev-test`
- Health: `http://localhost:5224/health`, `/health/live`, `/health/ready`

Frontend'i ayrı terminalde başlatın:

```powershell
dotnet run --project .\Frontend\OnleyiciBakimSistemi.csproj
```

Frontend: `http://localhost:5080`

## Veritabanı çalışma biçimi

- `BakimYonetimiDbContext`, teslim edilen 33 iş tablosunu ve ilişkilerini birebir eşler.
- Dashboard, makine, arıza ve bakım planı ekranları gerçek SQL Server yedeğini okur.
- Kaynak şema uygulama başlangıcında değiştirilmez; migration veya `EnsureCreated` çalıştırılmaz.
- Tarihsel örnek veri 2015'te bittiği için aylık grafik, boş görünmek yerine veri setinin son ayına
  göre son altı ayı gösterir.

PostgreSQL komut modeli Production/entegrasyon ortamında ayrıca yapılandırılabilir:

Production/entegrasyon ortamında:

```powershell
$env:Database__Provider="PostgreSql"
$env:ConnectionStrings__PostgreSql="Host=...;Port=5432;Database=...;Username=...;Password=..."
```

Uygulama başlangıçta migration veya destructive veritabanı işlemi çalıştırmaz. Nihai DDL/migration
veritabanı ekibinden uygulanmalıdır.

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
