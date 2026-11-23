param(
    [string]$BackupPath = "$env:USERPROFILE\Downloads\BakimYonetimiDb_before_20260805.bak"
)

$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$apiProject = Join-Path $repoRoot 'OnleyiciBakim\OnleyiciBakim.csproj'
$frontendProject = Join-Path $repoRoot 'Frontend\OnleyiciBakimSistemi.csproj'
$mlRoot = Join-Path $repoRoot 'MlService'
$python = Join-Path $mlRoot '.venv\Scripts\python.exe'
$databaseName = 'BakimYonetimiDb'
$sqlServer = '(localdb)\MSSQLLocalDB'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET SDK bulunamadı. .NET 8 SDK veya daha yenisini kurup tekrar deneyin.'
}
if (-not (Get-Command py -ErrorAction SilentlyContinue)) {
    throw 'Python Launcher bulunamadı. Python 3.11 kurup tekrar deneyin.'
}
if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
    throw 'sqlcmd bulunamadı. SQL Server LocalDB komut satırı araçlarını kurup tekrar deneyin.'
}

$sdkVersions = dotnet --list-sdks
if (-not ($sdkVersions | Where-Object { [version](($_ -split ' ')[0]) -ge [version]'8.0' })) {
    throw '.NET 8 SDK veya daha yeni bir SDK kurup tekrar deneyin.'
}

py -3.11 --version
if ($LASTEXITCODE -ne 0) {
    throw 'Python 3.11 bulunamadı. Python 3.11 kurup tekrar deneyin.'
}

$databaseExists = (& sqlcmd -S $sqlServer -E -h -1 -W -Q `
    "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.databases WHERE name=N'$databaseName'" | Out-String).Trim()
if ($LASTEXITCODE -ne 0) {
    throw 'SQL Server LocalDB başlatılamadı. SQL Server LocalDB kurulumunu kontrol edin.'
}
if ($databaseExists -ne '1') {
    if (-not (Test-Path -LiteralPath $BackupPath)) {
        throw "Veritabanı yedeği bulunamadı: $BackupPath . Yedeği edinin veya Start-Local.ps1 -BackupPath <dosya.bak> kullanın."
    }
    & (Join-Path $PSScriptRoot 'Restore-BakimYonetimiDb.ps1') -BackupPath $BackupPath
    if ($LASTEXITCODE -ne 0) { throw 'Veritabanı yedeği geri yüklenemedi.' }
}

& sqlcmd -S $sqlServer -d $databaseName -E -b -f 65001 `
    -i (Join-Path $PSScriptRoot '20260805_PreventiveMaintenanceCompletion.sql')
if ($LASTEXITCODE -ne 0) { throw 'Veritabanı tamamlama betiği uygulanamadı.' }

if (-not (Test-Path -LiteralPath $python)) {
    py -3.11 -m venv (Join-Path $mlRoot '.venv')
    if ($LASTEXITCODE -ne 0) { throw 'Python sanal ortamı oluşturulamadı.' }
}

& $python -m pip install -r (Join-Path $mlRoot 'requirements.txt')
if ($LASTEXITCODE -ne 0) { throw 'ML servisinin Python paketleri kurulamadı.' }

$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Database__UseBakimYonetimiReadModel = 'true'
$env:ASPNETCORE_URLS = 'http://localhost:5001'
$mlProcess = Start-Process -FilePath $python -ArgumentList @('app.py') -WorkingDirectory $mlRoot -PassThru

$env:ASPNETCORE_URLS = 'http://localhost:5224'
$apiProcess = Start-Process -FilePath 'dotnet' -ArgumentList @('run', '--project', $apiProject, '--no-launch-profile') -WorkingDirectory $repoRoot -PassThru

$env:ASPNETCORE_URLS = 'http://localhost:5080'
$frontendProcess = Start-Process -FilePath 'dotnet' -ArgumentList @('run', '--project', $frontendProject, '--no-launch-profile') -WorkingDirectory $repoRoot -PassThru

$apiReady = $false
for ($attempt = 0; $attempt -lt 60; $attempt++) {
    if ($apiProcess.HasExited) { throw "API başlatılamadı. Süreç kodu: $($apiProcess.ExitCode)" }
    try {
        Invoke-WebRequest -Uri 'http://localhost:5224/health/live' -TimeoutSec 2 | Out-Null
        $apiReady = $true
        break
    }
    catch { Start-Sleep -Seconds 1 }
}
if (-not $apiReady) { throw 'API 60 saniye içinde hazır olmadı; API penceresindeki hata mesajını kontrol edin.' }

Start-Process 'http://localhost:5080'
Write-Host 'Uygulama hazır: http://localhost:5080'
Write-Host 'API Swagger: http://localhost:5224/swagger'
Write-Host "ML, API ve web arayüzü PID değerleri: $($mlProcess.Id), $($apiProcess.Id), $($frontendProcess.Id)"
Write-Host 'Servisleri durdurmak için açılan üç konsol penceresini kapatın.'
