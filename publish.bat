@echo off
rem Pushes Duckr to https://github.com/brunoric3d/duckr_audio and, optionally,
rem tags a release so GitHub Actions builds the installer and publishes it.
cd /d "%~dp0"
echo.
echo  Pushing main to GitHub...
echo.
git push -u origin main
if errorlevel 1 (
  echo.
  echo  Push failed. If GitHub says the repo has files you don't have
  echo  (README/license created on the site), tell Claude.
  goto end
)
echo.
set TAG=
set /p TAG= Release version to publish (e.g. 0.1.0), or Enter to skip: 
if "%TAG%"=="" goto end
git tag v%TAG%
git push origin v%TAG%
echo.
echo  Tag v%TAG% pushed. GitHub Actions is now building the installer:
echo  https://github.com/brunoric3d/duckr_audio/actions
:end
echo.
pause
