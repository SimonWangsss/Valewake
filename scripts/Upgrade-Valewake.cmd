@echo off
setlocal
set "VALEWAKE_UPGRADE_SCRIPT=%~dp0Upgrade-Valewake.ps1"
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "try { & $env:VALEWAKE_UPGRADE_SCRIPT } catch { Write-Host ('Upgrade stopped: ' + $_.Exception.Message) -ForegroundColor Red; exit 1 }"
pause
