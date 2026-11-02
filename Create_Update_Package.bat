@echo off
setlocal
cd /d "%~dp0"
echo ========================================================
echo   Elmasry Accounting - Preparing Update Package
echo   شركة المصري للتجارة والتوريدات - تجهيز حزمة التحديث
echo ========================================================

set TARGET_DIR=%~dp0..\Update_For_Other_PC
if exist "%TARGET_DIR%" (
    rmdir /s /q "%TARGET_DIR%"
)
mkdir "%TARGET_DIR%"

robocopy "%~dp0..\App" "%TARGET_DIR%" /E /XF "appsettings.json" "appsettings.Development.json" > nul

echo.
echo ========================================================
echo   [SUCCESS] Update Package created successfully!
echo   Location: %TARGET_DIR%
echo.
echo   Steps for the other PC:
echo   1. Close Accountant_sys.exe on the other PC.
echo   2. Copy all files from Update_For_Other_PC to USB.
echo   3. Paste and Replace into the app folder on the other PC.
echo   4. Start Accountant_sys.exe.
echo ========================================================
pause
