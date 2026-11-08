@echo off
cd /d "%~dp0src\TeamManager.Web"
set ASPNETCORE_ENVIRONMENT=Development
if exist "%~dp0.tools\dotnet\dotnet.exe" (
  "%~dp0.tools\dotnet\dotnet.exe" run --urls "https://localhost:7043;http://localhost:5043"
) else (
  dotnet run --urls "https://localhost:7043;http://localhost:5043"
)
pause
