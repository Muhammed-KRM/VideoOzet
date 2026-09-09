# Java Kurulum Script'i
# Microsoft OpenJDK 17 otomatik kurulumu

Write-Host "Java kurulum kontrolü yapılıyor..." -ForegroundColor Green

# Java'nın zaten kurulu olup olmadığını kontrol et
try {
    $javaVersion = java -version 2>&1
    if ($javaVersion -match "openjdk|java") {
        Write-Host "Java zaten kurulu: $($javaVersion[0])" -ForegroundColor Yellow
        Write-Host "JAVA_HOME: $env:JAVA_HOME" -ForegroundColor Yellow
        
        $continue = Read-Host "Yine de yeni Java kurmak istiyor musunuz? (y/N)"
        if ($continue -ne "y" -and $continue -ne "Y") {
            exit 0
        }
    }
} catch {
    Write-Host "Java bulunamadı, kurulum başlatılıyor..." -ForegroundColor Yellow
}

# Microsoft OpenJDK 17 indirme URL'i
$jdkUrl = "https://aka.ms/download-jdk/microsoft-jdk-17.0.12-windows-x64.msi"
$jdkInstaller = "$env:TEMP\microsoft-jdk-17.msi"

Write-Host "Microsoft OpenJDK 17 indiriliyor..." -ForegroundColor Green
try {
    Invoke-WebRequest -Uri $jdkUrl -OutFile $jdkInstaller -UseBasicParsing
    Write-Host "İndirme tamamlandı." -ForegroundColor Green
} catch {
    Write-Host "İndirme hatası: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# MSI installer'ı çalıştır
Write-Host "Java kurulumu başlatılıyor..." -ForegroundColor Green
try {
    Start-Process -FilePath "msiexec.exe" -ArgumentList "/i", $jdkInstaller, "/quiet", "/norestart" -Wait
    Write-Host "Java kurulumu tamamlandı." -ForegroundColor Green
} catch {
    Write-Host "Kurulum hatası: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# Kurulum dosyasını temizle
Remove-Item $jdkInstaller -Force

# JAVA_HOME environment variable'ını ayarla
$javaPath = "C:\Program Files\Microsoft\jdk-17.0.12-hotspot"
if (Test-Path $javaPath) {
    Write-Host "JAVA_HOME ayarlanıyor: $javaPath" -ForegroundColor Green
    
    # System environment variable olarak ayarla
    [Environment]::SetEnvironmentVariable("JAVA_HOME", $javaPath, "Machine")
    
    # Mevcut session için ayarla
    $env:JAVA_HOME = $javaPath
    
    # PATH'e ekle
    $currentPath = [Environment]::GetEnvironmentVariable("PATH", "Machine")
    $javaBinPath = "$javaPath\bin"
    
    if ($currentPath -notlike "*$javaBinPath*") {
        $newPath = "$currentPath;$javaBinPath"
        [Environment]::SetEnvironmentVariable("PATH", $newPath, "Machine")
        $env:PATH = "$env:PATH;$javaBinPath"
        Write-Host "Java PATH'e eklendi." -ForegroundColor Green
    }
} else {
    Write-Host "Java kurulum dizini bulunamadı. Manuel kontrol gerekli." -ForegroundColor Red
    exit 1
}

# Android SDK environment variable'larını da ayarla
$androidSdkPath = "C:\Program Files (x86)\Android\android-sdk"
if (Test-Path $androidSdkPath) {
    Write-Host "ANDROID_HOME ayarlanıyor: $androidSdkPath" -ForegroundColor Green
    
    [Environment]::SetEnvironmentVariable("ANDROID_HOME", $androidSdkPath, "Machine")
    $env:ANDROID_HOME = $androidSdkPath
    
    # Android tools'ları PATH'e ekle
    $currentPath = [Environment]::GetEnvironmentVariable("PATH", "Machine")
    $androidPlatformTools = "$androidSdkPath\platform-tools"
    $androidCmdlineTools = "$androidSdkPath\cmdline-tools\latest\bin"
    
    $pathsToAdd = @($androidPlatformTools, $androidCmdlineTools)
    $newPath = $currentPath
    
    foreach ($pathToAdd in $pathsToAdd) {
        if (Test-Path $pathToAdd -and $currentPath -notlike "*$pathToAdd*") {
            $newPath = "$newPath;$pathToAdd"
        }
    }
    
    if ($newPath -ne $currentPath) {
        [Environment]::SetEnvironmentVariable("PATH", $newPath, "Machine")
        $env:PATH = $newPath
        Write-Host "Android tools PATH'e eklendi." -ForegroundColor Green
    }
}

Write-Host "`nKurulum tamamlandı! Yeni bir PowerShell penceresi açın ve şu komutları test edin:" -ForegroundColor Green
Write-Host "java -version" -ForegroundColor Cyan
Write-Host "adb version" -ForegroundColor Cyan
Write-Host "avdmanager list avd" -ForegroundColor Cyan

Write-Host "`nAndroid emulator kurulumu için şu adımları takip edin:" -ForegroundColor Yellow
Write-Host "1. Yeni PowerShell penceresi açın (admin olarak)" -ForegroundColor White
Write-Host "2. docs/android_emulator_setup_guide.md dosyasındaki adımları takip edin" -ForegroundColor White

Read-Host "`nDevam etmek için Enter'a basın"