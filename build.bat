@echo off
setlocal

set CONFIG=Debug
if /i "%1"=="release" set CONFIG=Release

echo ========================================
echo  Building TazUO (%CONFIG%)
echo ========================================
echo.

dotnet build ClassicUO.sln -c %CONFIG%

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo BUILD FAILED
    exit /b %ERRORLEVEL%
)

echo.
echo ========================================
echo  Build succeeded!
echo  Output: bin\%CONFIG%\
echo ========================================
