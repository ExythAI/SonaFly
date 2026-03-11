@echo off
title SonaFly Server + Admin UI
echo ========================================
echo   SonaFly Server + Admin Web UI
echo ========================================
echo.
echo Starting API server on http://0.0.0.0:5092
echo Starting Admin UI on https://localhost:65457
echo.

:: Tell Vite to proxy API calls to our HTTP server
set ASPNETCORE_URLS=http://localhost:5092

:: Start the Vite dev server for the admin UI in background
start "SonaFly Admin UI" cmd /c "cd /d "%~dp0SonaFlyUI\sonaflyui.client" && npm run dev"

:: Start the API server in foreground
cd /d "%~dp0SonaFlyUI\SonaFlyUI.Server"
dotnet run --no-launch-profile --urls http://0.0.0.0:5092
pause
