@echo off
setlocal
cd /d "%~dp0"
echo ========================================================
echo   Elmasry Accounting - Build and Publish App
echo   شركة المصري للتجارة والتوريدات - بناء ونشر التطبيق
echo ========================================================

echo Stopping running instances of Accountant_sys...
taskkill /F /IM Accountant_sys.exe 2>nul

echo Building and Publishing to ..\App ...
dotnet publish Accountant_sys\Accountant_sys.csproj -c Release -r win-x64 --self-contained true -o "..\App"

if %ERRORLEVEL% equ 0 (
    echo.
    echo ========================================================
    echo   [SUCCESS] App published successfully to C:\Sys_Acountant\App
    echo ========================================================
) else (
    echo.
    echo [ERROR] Publish failed with error code %ERRORLEVEL%
)

pause
