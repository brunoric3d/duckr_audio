@echo off
rem Builds the Duckr installer: releases\Duckr-win-Setup.exe
rem One click, per-user install to %LocalAppData%\Duckr, opens Duckr when done.
rem .NET is not bundled: the installer downloads the .NET 10 Desktop Runtime only if missing.
rem
rem   make-installer.bat          -> dev build, version 0.1.0-dev.<minutes>, always newer
rem                                  than the last one, so the installer updates it
rem   make-installer.bat 0.2.0    -> release build with that exact version
rem
rem If Duckr is already installed, the installer asks to overwrite/update: answer Yes.
rem Output goes to installer-log.txt in this folder.
cd /d "%~dp0"
set BASE_VERSION=0.1.0
set VPK_VERSION=1.2.161
set VPK=%USERPROFILE%\.dotnet\tools\vpk.exe

if "%~1"=="" (
  for /f "usebackq" %%m in (`powershell -NoProfile -Command "[int]((Get-Date) - [datetime]'2026-01-01').TotalMinutes"`) do set BUILDNUM=%%m
) else (
  set VERSION=%~1
)
if "%~1"=="" set VERSION=%BASE_VERSION%-dev.%BUILDNUM%

echo === %date% %time% === > installer-log.txt
echo VERSION=%VERSION% >> installer-log.txt
where dotnet >> installer-log.txt 2>&1 || (echo NO_DOTNET >> installer-log.txt & goto end)

echo --- installing packaging tool vpk %VPK_VERSION% >> installer-log.txt
dotnet tool update -g vpk --version %VPK_VERSION% >> installer-log.txt 2>&1
if not exist "%VPK%" (echo NO_VPK >> installer-log.txt & goto end)

echo --- publishing framework-dependent build >> installer-log.txt
if exist publish rmdir /s /q publish
dotnet publish src\Duckr\Duckr.csproj -c Release -r win-x64 --self-contained false -p:Version=%VERSION% -o publish -nologo >> installer-log.txt 2>&1
echo PUBLISH_EXIT=%errorlevel% >> installer-log.txt
if not exist publish\Duckr.exe goto end

echo --- packing installer >> installer-log.txt
"%VPK%" pack --packId Duckr --packVersion %VERSION% --packDir publish --mainExe Duckr.exe --framework net10.0-x64-desktop --packTitle Duckr --icon src\Duckr\Assets\duckr.ico --splashImage installer\splash.gif --outputDir releases >> installer-log.txt 2>&1
echo PACK_EXIT=%errorlevel% >> installer-log.txt

:end
echo DONE >> installer-log.txt
