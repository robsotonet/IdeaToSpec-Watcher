@echo off
REM run-hermes.cmd — on-demand run of the published Hermes app.
REM Used both manually and by the scheduled task. Runs in Production mode,
REM so it reads the Windows paths from appsettings.json (C:\DevWork\dockmaster-io\...).
setlocal
REM Force Production so a machine-level DOTNET_ENVIRONMENT=Development can't
REM redirect Hermes to the local test folders (./_localdata).
set DOTNET_ENVIRONMENT=Production
set SCRIPT_DIR=%~dp0
set EXE=%SCRIPT_DIR%..\publish\Hermes.exe

if not exist "%EXE%" (
  echo Hermes.exe not found at "%EXE%".
  echo Run scripts\publish.ps1 first.
  exit /b 1
)

"%EXE%" %*
exit /b %ERRORLEVEL%
