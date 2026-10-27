$ErrorActionPreference = "Stop"

$RootDir = $PSScriptRoot
$DistDir = Join-Path $RootDir "DagitimPaketi"
$AppDir = Join-Path $DistDir "App"
$WwwRootDir = Join-Path $RootDir "src\VideoOzet.API\wwwroot"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " VideoOzet AI - Docker'siz Tek Setup.exe Paketleme Araci  " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Eski dağıtım klasörünü temizle
if (Test-Path $DistDir) {
    Write-Host "[1/5] Eski DagitimPaketi klasoru temizleniyor..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force $DistDir
}
New-Item -ItemType Directory -Path $AppDir | Out-Null

# 2. Angular UI Production Build
Write-Host "[2/5] Angular Arayuzu (VideoOzet.UI) derleniyor..." -ForegroundColor Yellow
& npm.cmd --prefix (Join-Path $RootDir "src\VideoOzet.UI") run build

# 3. Derlenen Angular dosyalarını API'nin wwwroot klasörüne kopyala
Write-Host "[3/5] Arayuz dosyalari API icine gomuluyor (wwwroot)..." -ForegroundColor Yellow
if (Test-Path $WwwRootDir) {
    Remove-Item -Recurse -Force $WwwRootDir
}
New-Item -ItemType Directory -Path $WwwRootDir | Out-Null
$UiBrowserDist = Join-Path $RootDir "src\VideoOzet.UI\dist\video-ozet.ui\browser"
Copy-Item -Path "$UiBrowserDist\*" -Destination $WwwRootDir -Recurse -Force

# 4. VideoOzet.API + Gomulu Worker + SQLite -> Tek .EXE olarak yayınla
Write-Host "[4/5] Bagimsiz Masaustu Uygulamasi (VideoOzet.API.exe) uretiliyor..." -ForegroundColor Yellow
dotnet publish (Join-Path $RootDir "src\VideoOzet.API\VideoOzet.API.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $AppDir

# Geliştirme ayarlarını sil ve bağımsız masaüstü (Standalone) appsettings.json oluştur
$DevConfig = Join-Path $AppDir "appsettings.Development.json"
if (Test-Path $DevConfig) {
    Remove-Item -Force $DevConfig
}

$StandaloneAppSettings = @"
{
  "StandaloneMode": true,
  "AutoOpenBrowser": true,
  "Urls": "http://localhost:5002",
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=videoozet.db"
  },
  "ApiKey": "SUPER_SECRET_API_KEY_123!",
  "GEMINI_API_KEY": "",
  "GEMINI_MODEL": "gemini-3.8-flash-tiered",
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
"@
Set-Content -Path (Join-Path $AppDir "appsettings.json") -Value $StandaloneAppSettings -Encoding UTF8

# FFmpeg kopyala
$FfmpegSource = Join-Path $RootDir "ffmpeg\bin"
if (Test-Path $FfmpegSource) {
    $FfmpegDest = Join-Path $AppDir "ffmpeg\bin"
    New-Item -ItemType Directory -Path $FfmpegDest -Force | Out-Null
    Copy-Item -Path "$FfmpegSource\*" -Destination $FfmpegDest -Recurse -Force
}

# 5. Inno Setup ile VideoOzet_Setup.exe kurulum sihirbazını derle
Write-Host "[5/5] Windows Kurulum Sihirbazi (VideoOzet_Setup.exe) olusturuluyor..." -ForegroundColor Yellow
$IsccPaths = @(
    "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
)
$IsccExe = $IsccPaths | Where-Object { Test-Path $_ } | Select-Object -First 1

if ($IsccExe) {
    & $IsccExe (Join-Path $RootDir "installer.iss")
    Write-Host ""
    Write-Host "==========================================================" -ForegroundColor Green
    Write-Host " KURULUM DOSYASI HAZIR! " -ForegroundColor Green
    Write-Host " Setup Dosyasi: $(Join-Path $DistDir 'VideoOzet_Setup.exe')" -ForegroundColor Green
    Write-Host "==========================================================" -ForegroundColor Green
} else {
    Write-Warning "Inno Setup (ISCC.exe) bulunamadi. Uygulama klasoru hazir: $AppDir"
}
