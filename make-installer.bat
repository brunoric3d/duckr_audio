@echo off
rem Builds the Duckr installer: releases\Duckr-win-Setup.exe
rem One click, per-user install to %LocalAppData%\Duckr, opens Duckr when done.
rem .NET is included, so it works on any Windows 10/11 PC.
rem Bump VERSION (and <Version> in src\Duckr\Duckr.csproj) for every new release.
rem Output goes to installer-log.txt in this folder.
cd /d "%~dp0"
set VERSION=0.1.0
set VPK_VERSION=1.2.161
set VPK=%USERPROFILE%\.dotnet\tools\vpk.exe

echo === %date% %time% === > installer-log.txt
where dotnet >> installer-log.txt 2>&1 || (echo NO_DOTNET >> installer-log.txt & goto end)

echo --- installing packaging tool vpk %VPK_VERSION% >> installer-log.txt
dotnet tool update -g vpk --version %VPK_VERSION% >> installer-log.txt 2>&1
if not exist "%VPK%" (echo NO_VPK >> installer-log.txt & goto end)

echo --- publishing self-contained build >> installer-log.txt
if exist publish rmdir /s /q publish
dotnet publish src\Duckr\Duckr.csproj -c Release -r win-x64 --self-contained true -p:Version=%VERSION% -o publish -nologo >> installer-log.txt 2>&1
echo PUBLISH_EXIT=%errorlevel% >> installer-log.txt
if not exist publish\Duckr.exe goto end

echo --- packing installer >> installer-log.txt
"%VPK%" pack --packId Duckr --packVersion %VERSION% --packDir publish --mainExe Duckr.exe --packTitle Duckr --icon src\Duckr\Assets\duckr.ico --splashImage installer\splash.gif --outputDir releases >> installer-log.txt 2>&1
echo PACK_EXIT=%errorlevel% >> installer-log.txt

:end
echo DONE >> installer-log.txt
