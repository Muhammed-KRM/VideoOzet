@echo off
title OzelDers - Android Emulator Baslatici
color 0A

:: ── Ortam Degiskenleri ──────────────────────────────────────────
set ANDROID_HOME=C:\Program Files (x86)\Android\android-sdk
set JAVA_HOME=C:\Program Files\Android\Android Studio\jbr
set PATH=%JAVA_HOME%\bin;%ANDROID_HOME%\platform-tools;%ANDROID_HOME%\emulator;%ANDROID_HOME%\cmdline-tools\latest\bin;%PATH%

set ADB=%ANDROID_HOME%\platform-tools\adb.exe
set EMULATOR=%ANDROID_HOME%\emulator\emulator.exe
set AVD_NAME=Pixel_5

:: ── Kontroller ──────────────────────────────────────────────────
echo [1/4] Ortam kontrol ediliyor...

if not exist "%EMULATOR%" (
    echo HATA: Emulator bulunamadi: %EMULATOR%
    echo Android SDK kurulu mu? ANDROID_HOME degiskenini kontrol edin.
    pause
    exit /b 1
)

if not exist "%ADB%" (
    echo HATA: ADB bulunamadi: %ADB%
    pause
    exit /b 1
)

:: ── Emulator Zaten Calisiyor mu? ────────────────────────────────
echo [2/4] Emulator durumu kontrol ediliyor...
"%ADB%" devices | findstr "emulator" >nul 2>&1
if %errorlevel% == 0 (
    echo Emulator zaten calisiyor, atlanıyor...
    goto :deploy
)

:: ── Emulator Baslat ─────────────────────────────────────────────
echo [3/4] Emulator baslatiliyor: %AVD_NAME%
start "" "%EMULATOR%" -avd %AVD_NAME% -no-snapshot-load

echo Emulator baslamasi bekleniyor (30 saniye)...
timeout /t 30 /nobreak >nul

:: ADB boot tamamlanana kadar bekle
:wait_boot
"%ADB%" shell getprop sys.boot_completed 2>nul | findstr "1" >nul
if %errorlevel% neq 0 (
    echo Cihaz hazirlanıyor...
    timeout /t 5 /nobreak >nul
    goto :wait_boot
)
echo Emulator hazir!

:: ── MAUI Uygulamasini Deploy Et ─────────────────────────────────
:deploy
echo [4/4] OzelDers.App Android'e deploy ediliyor...
echo Bu islem birkaç dakika surebilir...

dotnet build src/OzelDers.App/OzelDers.App.csproj -f net10.0-android -c Debug

if %errorlevel% neq 0 (
    echo HATA: Build basarisiz oldu!
    pause
    exit /b 1
)

:: APK'yi yukle
set APK_PATH=src\OzelDers.App\bin\Debug\net10.0-android\com.companyname.ozelders.app-Signed.apk
if exist "%APK_PATH%" (
    echo APK yukleniyor...
    "%ADB%" install -r "%APK_PATH%"
    echo Uygulama baslatiliyor...
    "%ADB%" shell am start -n com.companyname.ozelders.app/crc64.MainActivity
) else (
    echo APK bulunamadi, dotnet run ile deneniyor...
    dotnet run --project src/OzelDers.App/OzelDers.App.csproj -f net10.0-android
)

echo.
echo ✓ Tamamlandi! OzelDers.App Android emulatorunde calisiyor.
echo.
pause
