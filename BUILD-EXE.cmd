@echo off
setlocal
cd /d "%~dp0"
where dotnet >nul 2>nul || (echo Install .NET 10 SDK x64 first. & pause & exit /b 1)
where swift >nul 2>nul || (echo Install Swift 6.3.3 for Windows x64 first. & pause & exit /b 1)
where pwsh >nul 2>nul || (echo Install PowerShell 7 first. & pause & exit /b 1)
dotnet run --project windows/CodexAccountSwitcher.UiChecks -c Release
if errorlevel 1 goto failed
pwsh -NoProfile -File scripts/package-windows.ps1
if errorlevel 1 goto failed
echo EXE: windows\artifacts\Codex-Account-Switcher-windows-x64.exe
explorer "%CD%\windows\artifacts"
pause
exit /b 0
:failed
echo Build or UI checks failed. Read the error above.
pause
exit /b 1
