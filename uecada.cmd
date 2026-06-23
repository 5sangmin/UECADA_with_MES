@echo off
REM ---------------------------------------------------------------------------
REM uecada — one-stop CLI wrapper for the UECADA stack.
REM
REM 실제 로직은 scripts\uecada.ps1 에 있다. 이 cmd 는 PowerShell 을 호출하는
REM 얇은 wrapper 일 뿐이다. 별도 PowerShell 창을 띄우지 않고, 현재 cmd 창에
REM 그대로 출력하기 위해 -NoLogo -NoProfile -ExecutionPolicy Bypass 를 쓴다.
REM
REM Usage:
REM   uecada start                    : 모든 컴포넌트 기동 (default)
REM   uecada start <component>        : 특정 컴포넌트만 기동
REM   uecada stop  [<component>]
REM   uecada restart [<component>]
REM   uecada status
REM   uecada logs <component>
REM
REM Components:
REM   infra        : root docker compose (mysql, timescaledb, commanddb, ai-api, backend)
REM   das          : total_das\DAS         (das-mosquitto, das-node-red, das-simulator)
REM   equip-sim    : total_das\equip-sim   (nodered-line01/02/03)
REM   command-center : total_das\equip-sim (command-center node-red + OPC UA server)
REM   xdas         : total_das\X_DAS       (x-das-node-red)
REM   backend-api  : unreal-backend\BeApi  (dotnet run on Windows, NOT docker)
REM   frontend     : frontend\UECADA_3     (npm run dev on Windows, NOT docker)
REM ---------------------------------------------------------------------------

setlocal
set "SCRIPT_DIR=%~dp0"
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%scripts\uecada.ps1" %*
exit /b %ERRORLEVEL%
