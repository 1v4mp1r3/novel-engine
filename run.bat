@echo off
cd /d "%~dp0"
dotnet run -c Release --project src\NovelEngine.Editor\NovelEngine.Editor.csproj
if errorlevel 1 pause
