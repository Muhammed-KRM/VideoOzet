$ErrorActionPreference = "Stop"

$RootDir = $PSScriptRoot
$DistDir = Join-Path $RootDir "DagitimPaketi"
$ApiDir = Join-Path $DistDir "API"
$WorkerDir = Join-Path $DistDir "Worker"
$WwwRootDir = Join-Path $RootDir "src\VideoOzet.API\wwwroot"

Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host " VideoOzet - Kaynak Kodsuz Tek .EXE Paketleme Araci " -ForegroundColor Cyan
Write-Host "=====================================================" -ForegroundColor Cyan

# 1. Eski dağıtım klasörünü temizle
if (Test-Path $DistDir) {
    Write-Host "[1/6] Eski DagitimPaketi klasoru temizleniyor..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force $DistDir
}
New-Item -ItemType Directory -Path $ApiDir | Out-Null
New-Item -ItemType Directory -Path $WorkerDir | Out-Null

# 2. Angular UI Production Build (Kaynak kodları minify/bundle eder)
Write-Host "[2/6] Angular Arayuzu (VideoOzet.UI) derleniyor..." -ForegroundColor Yellow
& npm.cmd --prefix (Join-Path $RootDir "src\VideoOzet.UI") run build

# 3. Derlenen Angular dosyalarını API'nin wwwroot klasörüne kopyala
Write-Host "[3/6] Arayuz dosyalari API icine gomuluyor (wwwroot)..." -ForegroundColor Yellow
if (Test-Path $WwwRootDir) {
    Remove-Item -Recurse -Force $WwwRootDir
}
New-Item -ItemType Directory -Path $WwwRootDir | Out-Null
$UiBrowserDist = Join-Path $RootDir "src\VideoOzet.UI\dist\video-ozet.ui\browser"
Copy-Item -Path "$UiBrowserDist\*" -Destination $WwwRootDir -Recurse -Force

# 4. VideoOzet.API -> Tek .EXE olarak yayınla
Write-Host "[4/6] VideoOzet.API.exe (Self-Contained Single File) uretiliyor..." -ForegroundColor Yellow
dotnet publish (Join-Path $RootDir "src\VideoOzet.API\VideoOzet.API.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $ApiDir

# 5. VideoOzet.Worker -> Tek .EXE olarak yayınla
Write-Host "[5/6] VideoOzet.Worker.exe (Self-Contained Single File) uretiliyor..." -ForegroundColor Yellow
dotnet publish (Join-Path $RootDir "src\VideoOzet.Worker\VideoOzet.Worker.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $WorkerDir

# 6. FFmpeg, Altyapı docker-compose.yml ve Baslat.bat dosyalarını hazırla
Write-Host "[6/6] FFmpeg ve tek tikla calistirma betikleri ekleniyor..." -ForegroundColor Yellow
$FfmpegSource = Join-Path $RootDir "ffmpeg\bin"
if (Test-Path $FfmpegSource) {
    $FfmpegDest = Join-Path $DistDir "ffmpeg\bin"
    New-Item -ItemType Directory -Path $FfmpegDest -Force | Out-Null
    Copy-Item -Path "$FfmpegSource\*" -Destination $FfmpegDest -Recurse -Force
}

$DockerComposeContent = @"
services:
  postgres:
    image: pgvector/pgvector:pg16
    ports: ["5433:5432"]
    volumes: [pgdata:/var/lib/postgresql/data]
    environment:
      POSTGRES_DB: videoozet
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres
    restart: unless-stopped

  redis:
    image: redis:7-alpine
    ports: ["6380:6379"]
    restart: unless-stopped

  rabbitmq:
    image: rabbitmq:3-management
    ports: ["5673:5672", "15673:15672"]
    restart: unless-stopped

  minio:
    image: minio/minio:latest
    ports:
      - "9002:9000"
      - "9003:9001"
    environment:
      - MINIO_ROOT_USER=minioadmin
      - MINIO_ROOT_PASSWORD=minioadmin
    command: server /data --console-address ":9001"
    volumes:
      - minio_data:/data
    restart: unless-stopped

  ollama:
    image: ollama/ollama:latest
    ports: ["11435:11434"]
    volumes: [ollama_data:/root/.ollama]
    environment:
      - OLLAMA_KEEP_ALIVE=24h
    restart: unless-stopped

volumes:
  pgdata:
  ollama_data:
  minio_data:
"@
Set-Content -Path (Join-Path $DistDir "docker-compose.yml") -Value $DockerComposeContent -Encoding UTF8

$BaslatBatContent = @"
@echo off
chcp 65001 >nul
title VideoOzet AI Baslatici
echo ========================================================
echo   VideoOzet AI - Sistem Baslatiliyor...
echo ========================================================
echo.
echo [1/3] Altyapi servisleri (PostgreSQL, Redis, RabbitMQ, MinIO) kontrol ediliyor...
docker compose up -d
timeout /t 4 /nobreak >nul

echo [2/3] VideoOzet Worker (Arka Plan Islemcisi) baslatiliyor...
start "VideoOzet Worker" /D "%~dp0Worker" "%~dp0Worker\VideoOzet.Worker.exe"

echo [3/3] VideoOzet API ve Web Arayuzu (Port 5002) baslatiliyor...
start "VideoOzet API & Web UI" /D "%~dp0API" "%~dp0API\VideoOzet.API.exe" --urls "http://localhost:5002"

timeout /t 3 /nobreak >nul
echo.
echo Tarayici aciliyor: http://localhost:5002
start http://localhost:5002
echo.
echo Giris Anahtari (Varsayilan): SUPER_SECRET_API_KEY_123!
echo Uygulamayi kapatmak icin acilan VideoOzet pencerelerini kapatabilir veya Durdur.bat calistirabilirsiniz.
pause
"@
Set-Content -Path (Join-Path $DistDir "Baslat.bat") -Value $BaslatBatContent -Encoding UTF8

$DurdurBatContent = @"
@echo off
chcp 65001 >nul
echo VideoOzet servisleri durduruluyor...
taskkill /F /IM VideoOzet.API.exe >nul 2>&1
taskkill /F /IM VideoOzet.Worker.exe >nul 2>&1
docker compose stop
echo Tum servisler durduruldu.
pause
"@
Set-Content -Path (Join-Path $DistDir "Durdur.bat") -Value $DurdurBatContent -Encoding UTF8

Write-Host ""
Write-Host "=====================================================" -ForegroundColor Green
Write-Host " PAKETLEME TAMAMLANDI! " -ForegroundColor Green
Write-Host " Dagitim Klasoru: $DistDir" -ForegroundColor Green
Write-Host "=====================================================" -ForegroundColor Green
