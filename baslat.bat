@echo off
cls
echo ========================================================
echo       Video Ozet Sistemi Baslatiliyor...
echo ========================================================
echo.

echo [1/3] Docker servisleri baslatiliyor...
docker-compose -f docker-compose.dev.yml up -d

echo.
echo [2/3] Servislerin hazir olmasi bekleniyor...
ping 127.0.0.1 -n 5 > nul

echo.
echo [3/3] API, Worker ve Frontend pencereleri aciliyor...

start "VideoOzet-API" cmd /k "cd /d "%~dp0src\VideoOzet.API" && dotnet run"
start "VideoOzet-Worker" cmd /k "cd /d "%~dp0src\VideoOzet.Worker" && dotnet run"
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
