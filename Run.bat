@echo off
:: Heimdall.Next — Quick launch (Debug)
:: Builds and runs the app in Debug mode for testing changes.

cd /d "%~dp0"
dotnet run --project src\Heimdall.App\Heimdall.App.csproj
if %ERRORLEVEL% NEQ 0 pause
