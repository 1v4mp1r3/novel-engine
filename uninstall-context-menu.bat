@echo off
cd /d "%~dp0"
powershell -ExecutionPolicy Bypass -File tools\uninstall-explorer-context-menu.ps1
if errorlevel 1 pause
