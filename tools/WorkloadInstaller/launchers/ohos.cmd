@echo off
setlocal DisableDelayedExpansion
set "host=dotnet"
if defined DOTNET_ROOT if exist "%DOTNET_ROOT%\dotnet.exe" set "host=%DOTNET_ROOT%\dotnet.exe"
"%host%" "%~dp0..\any\WorkloadInstaller.dll" %*
exit /b %errorlevel%
