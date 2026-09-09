@echo off
echo ========================================================
echo       OzelDers.com Platformu Baslatiliyor...
echo ========================================================
echo.
echo Docker imajlari derleniyor ve servisler (Nginx, API, Web, Postgres vb.) ayaklandiriliyor...
echo Bu islem internet hizina gore biraz srebilir, lutfen pencereyi kapatmayin.
echo.

docker-compose up --build -d

echo.
echo ========================================================
echo Sistemin tam olarak hazir olmasi (Veritabaninin acilmasi) icin 10 saniye bekleniyor...
ping -n 11 127.0.0.1 > nul

echo Tarayicilarda Web Sitesi ve Swagger API Ekrani aciliyor...
start http://localhost
start http://localhost:5001/swagger/index.html

echo.
echo Basariyla tamamlandi! Tarayicilarinizi kontrol edebilirsiniz.
echo Bu pencereyi kapatabilirsiniz (Arka planda server calismaya devam edecektir).
echo ========================================================
pause
