@echo off
:: Heimdall.Next - Quick launch (Debug)
:: Builds and runs the app in Debug mode.
::
:: Usage:
::   Run.bat                    Build and run
::   Run.bat pull               git pull --ff-only before build
::   Run.bat clean              Wipe bin/ and obj/ before build
::   Run.bat pull clean         Both
::   Run.bat -- --some-arg ...  Forward args after -- to the app

setlocal enabledelayedexpansion
cd /d "%~dp0"
title Heimdall.Next - Debug

:: Detect double-click launch (Explorer wraps the call in cmd.exe /c "...").
:: When set, all exit points hit a final pause so the user can read output.
set DOUBLE_CLICK=
echo %CMDCMDLINE% | find /i "/c" >nul && set DOUBLE_CLICK=1

:: ----- argument parsing ----------------------------------------------------
set CLEAN=
set PULL=
set PASSTHROUGH=
:parse_args
if "%~1"=="" goto :args_done
if /i "%~1"=="clean" (set CLEAN=1 & shift & goto :parse_args)
if /i "%~1"=="pull"  (set PULL=1  & shift & goto :parse_args)
if "%~1"=="--" (shift & goto :passthrough_loop)
echo Unknown flag: %~1
echo Usage: Run.bat [pull] [clean] [-- args-for-app]
set EXIT=2
goto :end
:passthrough_loop
if "%~1"=="" goto :args_done
set PASSTHROUGH=!PASSTHROUGH! "%~1"
shift
goto :passthrough_loop
:args_done

:: ----- optional fast-forward pull -----------------------------------------
if defined PULL (
    echo Pulling latest from origin...
    git pull --ff-only
    echo.
)

:: ----- git banner ----------------------------------------------------------
for /f "delims=" %%a in ('git rev-parse --abbrev-ref HEAD 2^>nul') do set BRANCH=%%a
for /f "delims=" %%a in ('git rev-parse --short HEAD 2^>nul')      do set SHA=%%a
for /f "delims=" %%a in ('git log -1 --format^=%%s 2^>nul')        do set SUBJECT=%%a

set BEHIND=0
set AHEAD=0
set DIRTY=0
for /f %%a in ('git rev-list --count HEAD..origin/!BRANCH! 2^>nul') do set BEHIND=%%a
for /f %%a in ('git rev-list --count origin/!BRANCH!..HEAD 2^>nul') do set AHEAD=%%a
for /f %%a in ('git status --porcelain 2^>nul ^| find /c /v ""')    do set DIRTY=%%a

echo ----------------------------------------------------------------
echo  Heimdall.Next - Quick launch (Debug)
echo  Branch: !BRANCH! @ !SHA!
if defined SUBJECT echo  Last:   !SUBJECT!
if not "!BEHIND!"=="0" (
    echo  WARN:   local is !BEHIND! commit^(s^) BEHIND origin/!BRANCH! - run 'Run.bat pull' to update
)
if not "!AHEAD!"=="0" (
    echo  Info:   local is !AHEAD! commit^(s^) ahead of origin/!BRANCH!
)
if not "!DIRTY!"=="0" (
    echo  Dirty:  !DIRTY! file^(s^) with uncommitted changes
)
echo ----------------------------------------------------------------
echo.

:: ----- clean ---------------------------------------------------------------
if defined CLEAN (
    echo Cleaning bin\ and obj\ trees...
    for /d /r %%d in (bin obj) do if exist "%%d" rmdir /s /q "%%d"
    echo Done.
    echo.
)

:: ----- run -----------------------------------------------------------------
dotnet run --nologo --project src\Heimdall.App\Heimdall.App.csproj !PASSTHROUGH!
set EXIT=%ERRORLEVEL%
if %EXIT% NEQ 0 (
    echo.
    echo Run failed with exit code %EXIT%.
)

:end
:: From a terminal, exit silently on success and only pause on failure
:: (preserves scriptable use). From a double-click, always pause so the
:: banner, dotnet output and any error are readable before the window
:: disappears.
if defined DOUBLE_CLICK (
    echo.
    pause
) else if not "%EXIT%"=="0" (
    pause
)
exit /b %EXIT%
