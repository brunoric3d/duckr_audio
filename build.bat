@echo off
rem Builds Duckr and runs the tests. Output goes to build-log.txt in this folder.
cd /d "%~dp0"
echo === %date% %time% === > build-log.txt
where dotnet >> build-log.txt 2>&1 || (echo NO_DOTNET >> build-log.txt & goto end)
dotnet build Duckr.sln -c Release -nologo >> build-log.txt 2>&1
echo BUILD_EXIT=%errorlevel% >> build-log.txt
dotnet test tests\Duckr.Tests\Duckr.Tests.csproj -c Release --no-build -nologo >> build-log.txt 2>&1
echo TEST_EXIT=%errorlevel% >> build-log.txt
:end
echo DONE >> build-log.txt
