@echo off
setlocal
cd /d "%~dp0backend"
if exist ".venv\Scripts\pythonw.exe" (
  start "" ".venv\Scripts\pythonw.exe" "tools\standalone_chat.py"
  exit /b 0
)
python "tools\standalone_chat.py"
