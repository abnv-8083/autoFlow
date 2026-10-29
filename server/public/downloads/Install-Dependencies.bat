@echo off
setlocal enabledelayedexpansion
title Remote Laptop Control - Dependency Setup

echo =====================================================================
echo           Remote Laptop Control - Dependency Installer
echo =====================================================================
echo.

:: 1. Check if .NET 8 Desktop Runtime is already installed
echo [*] Checking for Microsoft .NET 8 Desktop Runtime...
dotnet --list-runtimes 2>nul | findstr /i "Microsoft.WindowsDesktop.App 8." >nul
if %errorlevel% equ 0 (
    echo [OK] Microsoft .NET 8 Desktop Runtime is already installed!
    goto :launch
)

echo [!] Microsoft .NET 8 Desktop Runtime was not detected on this PC.
echo [*] Installing Microsoft .NET 8 Desktop Runtime automatically...
echo.

:: 2. Try installing via Windows Package Manager (winget)
where winget >nul 2>nul
if %errorlevel% equ 0 (
    echo [*] Running winget installer (silent)...
    winget install --id Microsoft.DotNet.DesktopRuntime.8 -e --silent --accept-source-agreements --accept-package-agreements
    if %errorlevel% equ 0 (
        echo [OK] .NET 8 Desktop Runtime successfully installed via winget!
        goto :launch
    )
)

:: 3. Direct official Microsoft installer fallback
echo [*] Downloading official Microsoft .NET 8 Desktop Runtime installer...
set "TEMP_INSTALLER=%TEMP%\windowsdesktop-runtime-8.exe"
powershell -NoProfile -ExecutionPolicy Bypass -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; (New-Object Net.WebClient).DownloadFile('https://download.visualstudio.microsoft.com/download/pr/49520a32-9cb7-4f67-a2f0-58ce3e9be6fb/e04543d3b76cae93fb2ff067bebc66c9/windowsdesktop-runtime-8.0.12-win-x64.exe', '%TEMP_INSTALLER%')"

if exist "%TEMP_INSTALLER%" (
    echo [*] Installing .NET 8 Desktop Runtime...
    start /wait "" "%TEMP_INSTALLER%" /install /quiet /norestart
    echo [OK] .NET 8 Desktop Runtime installation completed!
) else (
    echo [ERROR] Could not download .NET runtime. Opening Microsoft download page in browser...
    start https://dotnet.microsoft.com/en-us/download/dotnet/8.0/runtime
)

:launch
echo.
echo =====================================================================
echo [*] Launching Remote Laptop Control Desktop Agent...
echo =====================================================================
start "" "%~dp0RemoteLaptopAgent.exe"
echo [OK] Agent is now running in your desktop and system tray!
timeout /t 3 >nul
