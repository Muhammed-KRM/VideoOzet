@echo off
cls
echo ========================================================
echo       Video Ozet Sistemi Baslatiliyor...
echo ========================================================
echo.

echo [1/4] Eski acik servisler temizleniyor (Eger varsa)...
taskkill /F /IM "dotnet.exe" /T > nul 2>&1
taskkill /F /IM "node.exe" /T > nul 2>&1
taskkill /F /IM "VideoOzet.API.exe" /T > nul 2>&1
taskkill /F /IM "VideoOzet.Worker.exe" /T > nul 2>&1
taskkill /FI "WINDOWTITLE eq VideoOzet-*" /F /T > nul 2>&1
echo.

echo [2/4] Docker servisleri baslatiliyor...
docker-compose -f docker-compose.dev.yml up -d

echo.
echo [3/4] Servislerin hazir olmasi bekleniyor (RabbitMQ vb.)...
ping 127.0.0.1 -n 15 > nul


echo.
echo [3.5/4] Projeler derleniyor (dosya kilitlenme cakismasini onlemek icin)...
dotnet build "%~dp0VideoOzet.slnx" -c Debug
if %errorlevel% neq 0 (
    echo [HATA] Proje derlenemedi!
    pause
    exit /b %errorlevel%
)

echo.
echo [4/4] API, Worker ve Frontend pencereleri aciliyor...

start "VideoOzet-API" cmd /k "cd /d "%~dp0src\VideoOzet.API" && dotnet run --no-build"
start "VideoOzet-Worker" cmd /k "cd /d "%~dp0src\VideoOzet.Worker" && dotnet run --no-build"
start "VideoOzet-UI" cmd /k "cd /d "%~dp0src\VideoOzet.UI" && npm start"

echo.
echo Tarayici aciliyor...
ping 127.0.0.1 -n 7 > nul
start http://localhost:4200
start http://localhost:5001/swagger

echo.
echo ========================================================
echo Sistem Basariyla Baslatildi!
echo Arayuz:    http://localhost:4200
echo Swagger:   http://localhost:5001/swagger
echo MinIO:     http://localhost:9001 (minioadmin / minioadmin)
echo RabbitMQ:  http://localhost:15672 (guest / guest)
echo API Key:   SUPER_SECRET_API_KEY_123!
echo ========================================================
echo Bu pencereyi kapatabilirsiniz.
pause
