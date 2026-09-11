@echo off
cls
echo ========================================================
echo       Video Ozet Sistemi Durduruluyor...
echo ========================================================
echo.

echo Docker servisleri durduruluyor...
cd /d "%~dp0"
docker-compose -f docker-compose.dev.yml stop

echo.
echo Calisan konsol pencereleri kapatiliyor...
taskkill /FI "WINDOWTITLE eq VideoOzet-API*" /F /T > nul 2>&1
taskkill /FI "WINDOWTITLE eq VideoOzet-Worker*" /F /T > nul 2>&1
taskkill /FI "WINDOWTITLE eq VideoOzet-UI*" /F /T > nul 2>&1

echo.
echo ========================================================
echo Sistem basariyla durduruldu.
echo ========================================================
pause
