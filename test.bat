@echo off
cd /d "%~dp0"
powershell -ExecutionPolicy Bypass -File tools\test.ps1
if errorlevel 1 pause
