@echo off
REM Starts the .NET side of the A2A demo.
REM
REM   start.cmd                  the A2A hosted agent on http://localhost:5401
REM   start.cmd host             same thing, explicitly
REM   start.cmd client           the console client, interactive menu
REM   start.cmd client all       every demo, non-interactive
REM   start.cmd client card job  just those demos
REM
REM Configure a real model in A2A.Demo.HostedAgent\appsettings.Development.json
REM (and A2A.Demo.Console\appsettings.Development.json for the delegate demo).
REM Both are gitignored. See the repo README.

setlocal
cd /d "%~dp0"

set "TARGET=%~1"
if "%TARGET%"=="" set "TARGET=host"

if /i "%TARGET%"=="host" goto :host
if /i "%TARGET%"=="client" goto :client

echo Unknown target "%TARGET%". Use "host" or "client".
exit /b 1

:host
echo Starting the .NET A2A hosted agent on http://localhost:5401 ...
dotnet run --project A2A.Demo.HostedAgent --urls http://localhost:5401
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
if "%DEMOS%"=="" goto :run_client_interactive
dotnet run --project A2A.Demo.Console --%DEMOS%
exit /b %ERRORLEVEL%

:run_client_interactive
dotnet run --project A2A.Demo.Console
exit /b %ERRORLEVEL%
