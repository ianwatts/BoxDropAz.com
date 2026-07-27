@echo off
echo Deploying BoxDropAz to PRODUCTION...
echo.
echo   Site:    https://www.boxdropaz.com
echo   Stripe:  live keys from stripe-settings.prod.json
echo   Stack:   BoxDropAz-prod
echo.
echo This deploys the live site. Continue?
pause
powershell -ExecutionPolicy Bypass -File "%~dp0deploy-to.ps1" -Environment prod
if %errorlevel% neq 0 (
    echo.
    echo Deployment FAILED.
    pause
    exit /b %errorlevel%
)
echo.
echo Deployment Complete.
pause
