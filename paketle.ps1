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
if ($LASTEXITCODE -ne 0) { throw "Angular UI derlemesi basarisiz oldu!" }

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
if ($LASTEXITCODE -ne 0) { throw "dotnet publish basarisiz oldu!" }

# Geliştirme ayarlarını sil ve bağımsız masaüstü (Standalone) appsettings.json oluştur
$DevConfig = Join-Path $AppDir "appsettings.Development.json"
if (Test-Path $DevConfig) {
    Remove-Item -Force $DevConfig
}

$StandaloneAppSettings = @"
{
  "StandaloneMode": true,
  "AutoOpenBrowser": true,
  "Urls": "http://localhost:5050",
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=videoozet.db"
  },
  "ApiKey": "SUPER_SECRET_API_KEY_123!",
  "GEMINI_API_KEY": "",
  "GEMINI_MODEL": "gemini-3.8-flash-tiered",
  "GROQ_API_KEY": "",
  "STT_PROVIDER": "Auto",
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
    Write-Host " WINDOWS KURULUM DOSYASI HAZIR! " -ForegroundColor Green
    Write-Host " Setup Dosyasi: $(Join-Path $DistDir 'VideoOzet_Setup.exe')" -ForegroundColor Green
    Write-Host "==========================================================" -ForegroundColor Green
} else {
    Write-Warning "Inno Setup (ISCC.exe) bulunamadi. Uygulama klasoru hazir: $AppDir"
}

# 6. macOS Paketleri (Apple Silicon osx-arm64 + Intel osx-x64)
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$MacTargets = @(
    @{ Rid = "osx-arm64"; ZipName = "VideoOzet_Mac_AppleSilicon_M1_M2_M3_M4.zip"; Label = "AppleSilicon_M1_M2_M3_M4" },
    @{ Rid = "osx-x64";   ZipName = "VideoOzet_Mac_Intel_x64.zip";               Label = "Intel_x64" }
)

$InfoPlist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>VideoOzet AI</string>
    <key>CFBundleDisplayName</key>
    <string>VideoOzet AI</string>
    <key>CFBundleIdentifier</key>
    <string>com.videoozet.ai</string>
    <key>CFBundleVersion</key>
    <string>1.0.0</string>
    <key>CFBundleShortVersionString</key>
    <string>1.0.0</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleExecutable</key>
    <string>VideoOzetLauncher</string>
    <key>LSMinimumSystemVersion</key>
    <string>12.0</string>
    <key>NSHighResolutionCapable</key>
    <true/>
</dict>
</plist>
"@.Replace("`r`n", "`n")

$MacLauncher = @"
#!/bin/bash
DIR="`$( cd "`$( dirname "`${BASH_SOURCE[0]}" )" && pwd )"
APP_DIR="`$DIR/App"
cd "`$APP_DIR"

export PATH="/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin:/usr/sbin:/sbin:`$APP_DIR/ffmpeg/bin:`$PATH"

chmod +x "`$APP_DIR/VideoOzet.API" 2>/dev/null || true
if [ -d "`$APP_DIR/ffmpeg/bin" ]; then
    chmod +x "`$APP_DIR/ffmpeg/bin/"* 2>/dev/null || true
fi

if lsof -i :5050 >/dev/null 2>&1; then
    open "http://localhost:5050"
    exit 0
fi

./VideoOzet.API --urls "http://localhost:5050"
"@.Replace("`r`n", "`n")

$MacInstallCmd = @"
#!/bin/bash
clear
echo "========================================================"
echo "   VideoOzet AI - Guvenli macOS Kurulum Sihirbazi"
echo "========================================================"
echo ""

EXPECTED_HASH="90ed7e87a47396ab29f1031f8859bfba153ed2137b1704c3701cd69f362ce35e"
AUTH_OK=0

for ATTEMPT in 1 2 3; do
    read -s -p "Lutfen Kurulum Sifresini Girin (Deneme `$ATTEMPT/3): " USER_PASS
    echo ""
    INPUT_HASH=`$(printf "%s" "`$USER_PASS" | shasum -a 256 | awk '{print `$1}')
    if [ "`$INPUT_HASH" = "`$EXPECTED_HASH" ]; then
        AUTH_OK=1
        break
    else
        echo "HATA: Gecersiz kurulum sifresi!"
    fi
done

if [ "`$AUTH_OK" -ne 1 ]; then
    echo "Kurulum sifresi 3 kez hatali girildi. Kurulum iptal edildi."
    exit 1
fi

echo ""
echo "Sifre dogrulandi! Kurulum baslatiliyor..."

SCRIPT_DIR="`$( cd "`$( dirname "`${BASH_SOURCE[0]}" )" && pwd )"
PARENT_DIR="`$( dirname "`$SCRIPT_DIR" )"
SOURCE_BUNDLE="`$SCRIPT_DIR/VideoOzet AI.app"
TARGET_BUNDLE="/Applications/VideoOzet AI.app"

if [ ! -w "/Applications" ]; then
    TARGET_BUNDLE="`$HOME/Applications/VideoOzet AI.app"
    mkdir -p "`$HOME/Applications"
fi

echo "[1/4] Uygulama dosyalari sisteme kuruluyor..."
rm -rf "`$TARGET_BUNDLE" 2>/dev/null || true
cp -R "`$SOURCE_BUNDLE" "`$TARGET_BUNDLE"
APP_DIR="`$TARGET_BUNDLE/Contents/MacOS/App"

xattr -cr "`$TARGET_BUNDLE" 2>/dev/null || true
chmod +x "`$TARGET_BUNDLE/Contents/MacOS/VideoOzetLauncher" 2>/dev/null || true
chmod +x "`$APP_DIR/VideoOzet.API" 2>/dev/null || true

export PATH="/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin:/usr/sbin:/sbin:`$APP_DIR/ffmpeg/bin:`$PATH"

echo "[2/4] FFmpeg (Video -> Ses ayristirici) kontrol ediliyor..."
if command -v ffmpeg >/dev/null 2>&1 || [ -f "`$APP_DIR/ffmpeg/bin/ffmpeg" ]; then
    echo "      -> FFmpeg hazir."
else
    echo "      -> FFmpeg indiriliyor..."
    mkdir -p "`$APP_DIR/ffmpeg/bin"
    if command -v brew >/dev/null 2>&1; then
        brew install ffmpeg || true
    fi
    if ! command -v ffmpeg >/dev/null 2>&1; then
        curl -L -s "https://evermeet.cx/ffmpeg/getrelease/ffmpeg/zip" -o "/tmp/ffmpeg_mac.zip" && unzip -o -q "/tmp/ffmpeg_mac.zip" -d "`$APP_DIR/ffmpeg/bin" && chmod +x "`$APP_DIR/ffmpeg/bin/ffmpeg" || true
        curl -L -s "https://evermeet.cx/ffmpeg/getrelease/ffprobe/zip" -o "/tmp/ffprobe_mac.zip" && unzip -o -q "/tmp/ffprobe_mac.zip" -d "`$APP_DIR/ffmpeg/bin" && chmod +x "`$APP_DIR/ffmpeg/bin/ffprobe" || true
        rm -f /tmp/ffmpeg_mac.zip /tmp/ffprobe_mac.zip 2>/dev/null || true
        xattr -cr "`$APP_DIR/ffmpeg/bin" 2>/dev/null || true
    fi
fi

echo "[3/4] Bu Mac bilgisayarin donanim kilidi (HWID) muhurleniyor..."
printf "VZ-INSTALL-AUTH-2026-9F8E7D6C" > "`$APP_DIR/.install_ticket"
cd "`$APP_DIR"
./VideoOzet.API --seal-hardware
rm -f "`$APP_DIR/.install_ticket" 2>/dev/null || true

echo "[4/4] Kurulum paketi ve arsiv dosyalari kendini imha ediyor..."
rm -f "`$PARENT_DIR"/VideoOzet_Mac_*.zip 2>/dev/null || true
rm -f "`$HOME/Downloads"/VideoOzet_Mac_*.zip 2>/dev/null || true
rm -f "`$HOME/Desktop"/VideoOzet_Mac_*.zip 2>/dev/null || true
( sleep 2 && rm -rf "`$SCRIPT_DIR" ) &

echo ""
echo "Kurulum tamamlandi! VideoOzet AI baslatiliyor (http://localhost:5050)..."
cd "`$APP_DIR"
exec ./VideoOzet.API --urls "http://localhost:5050"
"@.Replace("`r`n", "`n")

function New-UnixExecutableZip {
    param(
        [string]$SourceDir,
        [string]$DestinationZip
    )
    if (Test-Path $DestinationZip) { Remove-Item -Force $DestinationZip }

    $pyScript = @'
import os, sys, zipfile

src_dir = sys.argv[1]
dst_zip = sys.argv[2]

with zipfile.ZipFile(dst_zip, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=6) as zf:
    for root, dirs, files in os.walk(src_dir):
        for f in files:
            full_path = os.path.join(root, f)
            rel = os.path.relpath(full_path, src_dir).replace("\\", "/")
            is_exec = (
                rel.endswith(".command")
                or rel.endswith("/VideoOzetLauncher")
                or rel.endswith("/VideoOzet.API")
                or rel.endswith("/VideoOzet.Worker")
                or "/ffmpeg/bin/" in rel
            )
            zi = zipfile.ZipInfo(rel)
            zi.create_system = 3  # Unix (macOS Archive Utility izinleri okumasi icin sart!)
            zi.external_attr = (0o100755 if is_exec else 0o100644) << 16
            with open(full_path, "rb") as fp:
                zf.writestr(zi, fp.read())
'@
    $tmpPy = Join-Path $env:TEMP "make_unix_zip.py"
    [System.IO.File]::WriteAllText($tmpPy, $pyScript, (New-Object System.Text.UTF8Encoding($false)))
    & python $tmpPy $SourceDir $DestinationZip
    if ($LASTEXITCODE -ne 0) { throw "Python Unix ZIP olusturma hatasi!" }
    Remove-Item -Force $tmpPy -ErrorAction SilentlyContinue
}

foreach ($mac in $MacTargets) {
    $rid = $mac.Rid
    $zipPath = Join-Path $DistDir $mac.ZipName
    $stageRoot = Join-Path $DistDir "_mac_stage_$rid"
    $bundleDir = Join-Path $stageRoot "VideoOzet AI.app"
    $contentsDir = Join-Path $bundleDir "Contents"
    $macOsDir = Join-Path $contentsDir "MacOS"
    $macAppDir = Join-Path $macOsDir "App"

    Write-Host "[6/6] macOS paketi derleniyor ($rid -> $($mac.ZipName))..." -ForegroundColor Yellow
    if (Test-Path $stageRoot) { Remove-Item -Recurse -Force $stageRoot }
    New-Item -ItemType Directory -Path $macAppDir -Force | Out-Null

    dotnet publish (Join-Path $RootDir "src\VideoOzet.API\VideoOzet.API.csproj") `
        -c Release `
        -r $rid `
        --self-contained true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $macAppDir
    if ($LASTEXITCODE -ne 0) { throw "macOS ($rid) dotnet publish basarisiz oldu!" }

    $macDevCfg = Join-Path $macAppDir "appsettings.Development.json"
    if (Test-Path $macDevCfg) { Remove-Item -Force $macDevCfg }
    [System.IO.File]::WriteAllText((Join-Path $macAppDir "appsettings.json"), $StandaloneAppSettings, (New-Object System.Text.UTF8Encoding($false)))

    [System.IO.File]::WriteAllText((Join-Path $contentsDir "Info.plist"), $InfoPlist, (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText((Join-Path $macOsDir "VideoOzetLauncher"), $MacLauncher, (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText((Join-Path $stageRoot "Kurulum_ve_Baslat.command"), $MacInstallCmd, (New-Object System.Text.UTF8Encoding($false)))

    $macReadme = @"
===================================================================
  VideoÖzet AI - Güvenli macOS Kurulum Kılavuzu ($($mac.Label))
===================================================================

1. KURULUM VE BAŞLATMA:
   - Klasör içindeki "Kurulum_ve_Baslat.command" dosyasına ÇİFT TIKLAYIN.
     (macOS uyarısı çıkarsa Sağ Tıklayıp -> "Aç / Open" seçin).
   - Ekranda sorulan Kurulum Şifresini girin ve Enter'a basın.

2. GÜVENLİK VE OTO-İMHA ÖZELLİĞİ:
   - Kurulum tamamlandığında uygulama yalnızca bu Mac bilgisayarın
     anakart kimliğinize (HWID) kilitlenir; başka bilgisayara kopyalanamaz.
   - Kurulum biter bitmez indirilen .zip dosyası ve kurulum klasörü
     otomatik olarak kendini siler.
   - Sonraki kullanımlarda Applications (Uygulamalar) klasöründeki
     "VideoOzet AI" uygulamasını doğrudan açabilirsiniz.
===================================================================
"@
    [System.IO.File]::WriteAllText((Join-Path $stageRoot "BENI_OKU_MAC.txt"), $macReadme, (New-Object System.Text.UTF8Encoding($false)))

    New-UnixExecutableZip -SourceDir $stageRoot -DestinationZip $zipPath
    Remove-Item -Recurse -Force $stageRoot
    Write-Host "      -> Hazir: $zipPath" -ForegroundColor Green
}
