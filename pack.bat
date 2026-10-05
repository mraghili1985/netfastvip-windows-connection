@echo off
chcp 65001 >nul
REM ============================================================
REM  NETFASTVIP portable ZIP builder
REM  Usage: pack.bat           (uses default version below)
REM         pack.bat 3.0.1    (override version)
REM ============================================================
setlocal
cd /d "%~dp0"
set VER=3.0.2
if not "%~1"=="" set VER=%~1
set PUB=bin\Release\net10.0-windows\win-x64\publish
set STAGE=stage
set ZIP=netfastvip.v%VER%.zip

echo [1/6] Cleaning old publish/staging output...
rmdir /s /q "%PUB%" 2>nul
rmdir /s /q "%STAGE%" 2>nul

echo [2/6] Publishing (self-contained win-x64, multi-file)...
dotnet publish -c Release -r win-x64 --self-contained true
if errorlevel 1 (
  echo.
  echo *** BUILD FAILED ***
  pause
  exit /b 1
)

REM Optional OpenVPN payload: portable folder (exe+DLLs+driver, devcon 64-bit)
REM داخل Data\openvpn\ می‌رود، همراه با بقیه‌ی فایل‌های Data
REM نصاب MSI دیگر لازم نیست: devcon.exe 64-bit به‌تنهایی درایور را درست نصب می‌کند
echo [3/6] Copying optional OpenVPN + WireGuard files...
if exist "openvpn\" (
  xcopy /e /i /y "openvpn" "%PUB%\Data\openvpn\" >nul
  echo   - openvpn folder copied
) else (
  echo   - no openvpn folder, skipping
)
if exist "wireguard\" (
  xcopy /e /i /y "wireguard" "%PUB%\Data\wireguard\" >nul
  echo   - wireguard folder copied
) else (
  echo   - no wireguard folder, skipping
)
if exist "sing-box\" (
  xcopy /e /i /y "sing-box" "%PUB%\Data\sing-box\" >nul
  echo   - sing-box folder copied
) else (
  echo   - no sing-box folder, skipping
)

REM از این‌جا دیگر تک‌فایلی نیستیم: خروجی publish شامل exe + انبوهی DLL رانتایم است.
REM همه‌ی این خروجی (به‌همراه پوشه‌ی Data داخلش) داخل یک زیرپوشه‌ی App جمع می‌شود تا
REM کاربر فقط یک لانچر و README.txt را در ریشه‌ی زیپ ببیند، نه ده‌ها فایل پراکنده.
echo [4/6] Building portable layout (App\ + launcher)...
mkdir "%STAGE%" 2>nul
mkdir "%STAGE%\App" 2>nul
xcopy /e /i /y "%PUB%\*" "%STAGE%\App\" >nul
if exist "%STAGE%\App\README.txt" copy /y "%STAGE%\App\README.txt" "%STAGE%\README.txt" >nul
REM مکث کوتاه: بلافاصله از xcopy، آنتی‌ویروس (مخصوصاً Defender) می‌توند فایل‌های
REM .exe/.dll تازه‌کپی‌شده را برای اسکن لحظه‌ای قفل کند (همین باعث خطای "used by another
REM process" در مرحله‌ی زیپ می‌شود)، یک مکث کوتاه صبر می‌کنیم.
timeout /t 2 /nobreak >nul

REM توجه: قبلاً اینجا یک شورتکات .lnk واقعی ساخته می‌شد، ولی بعضی آنتی‌ویروس‌ها فایل‌های .lnk را به‌عنوان
REM الگوی رایج بدافزارهای مبتنی بر شورتکات می‌شناسند و حذفش می‌کنند. به‌جایش یک
REM لانچر .vbs ساده می‌سازیم (بدون پنجره‌ی کنسول، بدون obfuscation) که با مسیر نسبی
REM خودش App\NETFASTVIP.exe را اجرا می‌کند — مستقل از اینکه زیپ کجا اکسترکت شده.
echo [5/6] Creating launcher (NETFASTVIP.vbs)...
powershell -NoProfile -ExecutionPolicy Bypass -File "build-launcher.ps1" -StagePath "%STAGE%"
if errorlevel 1 (
  echo *** LAUNCHER CREATION FAILED ***
  pause
  exit /b 1
)

REM به‌جای یک Compress-Archive ساده، از اسکریپتی با retry+delay استفاده می‌کنیم تا اگر آنتی‌ویروس هنوز
REM روی یک فایل قفل دارد، خودش را چند بار تکرار کند و با شکست مواتی مواجه نشود.
echo [6/6] Creating %ZIP% ...
powershell -NoProfile -ExecutionPolicy Bypass -File "zip-with-retry.ps1" -StagePath "%STAGE%" -ZipPath "%ZIP%"
if errorlevel 1 (
  echo *** ZIP FAILED ***
  echo اگر همچنان شکست خورد می‌خورد، یک استثنای موقت برای پوشه‌ی پروژه/stage در Windows Defender بزنید.
  pause
  exit /b 1
)

echo.
echo DONE: %ZIP%
echo   ریشه زیپ: NETFASTVIP.vbs (لانچر) + README.txt + پوشه App\ (فایل‌های برنامه)
echo   با دابل‌کلیک روی NETFASTVIP.vbs برنامه بی‌صدا (بدون پنجره‌ی کنسول) اجرا می‌شود.
echo   نکته: بعد از اکسترکت زیپ در یک پوشه غیر از مسیر بیلد، یک بار دابل‌کلیک NETFASTVIP.vbs را تست کنید.
pause
