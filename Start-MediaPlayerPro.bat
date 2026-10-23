@echo off
setlocal
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
  echo The .NET 10 SDK was not found. Install it from https://dotnet.microsoft.com/download/dotnet/10.0
  pause
  exit /b 1
)
echo Preparing Opsis Media Player Pro - the first build takes a little longer...
dotnet publish -c Release -r win-x64 --self-contained -nologo -v q
if errorlevel 1 (
  echo.
  echo Build failed. Please send the error message above.
  pause
  exit /b 1
)
start "" "%~dp0bin\Release\net10.0-windows\win-x64\publish\OpsisMediaPlayerPro.exe" %*
endlocal
