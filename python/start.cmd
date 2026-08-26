@echo off
REM Starts the Python side of the A2A demo, creating the venv on first run.
REM
REM   start.cmd                  the A2A hosted agent on http://localhost:5402
REM   start.cmd host             same thing, explicitly
REM   start.cmd client           interactive menu, against whatever A2A_BASE_URL points at
REM   start.cmd client all       every demo, non-interactive
REM   start.cmd client card job  just those demos
REM
REM Configure a real model by copying ..\.env.template to .env in this folder.
REM .env is gitignored. See the repo README.

setlocal
cd /d "%~dp0"

set "PY=.venv\Scripts\python.exe"

if not exist "%PY%" (
    echo Creating virtual environment in .venv ...
    python -m venv .venv || exit /b 1
    "%PY%" -m pip install --upgrade pip --quiet
    echo Installing requirements ...
    "%PY%" -m pip install --quiet -r requirements.txt || exit /b 1
)

set "TARGET=%~1"
if "%TARGET%"=="" set "TARGET=host"

if /i "%TARGET%"=="host" goto :host
if /i "%TARGET%"=="client" goto :client

echo Unknown target "%TARGET%". Use "host" or "client".
exit /b 1

:host
echo Starting the Python A2A hosted agent on http://localhost:5402 ...
"%PY%" a2a_host.py
exit /b %ERRORLEVEL%

:client
shift
set "DEMOS="

:collect
if "%~1"=="" goto :run_client
set "DEMOS=%DEMOS% %~1"
shift
goto :collect

:run_client
"%PY%" a2a_console.py%DEMOS%
exit /b %ERRORLEVEL%
