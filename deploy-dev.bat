@echo off
echo Deploying BoxDropAz to DEVELOPMENT...
echo.
echo   Site:    https://dev.boxdropaz.com
echo   Stripe:  test / sandbox keys from stripe-settings.dev.json
echo   Stack:   BoxDropAz-dev
echo.
powershell -ExecutionPolicy Bypass -File "%~dp0deploy-to.ps1" -Environment dev
if %errorlevel% neq 0 (
    echo.
    echo Deployment FAILED.
    pause
    exit /b %errorlevel%
)
echo.
echo Deployment Complete.
pause
