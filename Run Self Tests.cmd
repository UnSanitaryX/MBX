@echo off
cd /d "%~dp0"
ModbusEmulatorX.Tests.exe
set "RESULT=%errorlevel%"
echo.
pause
exit /b %RESULT%
