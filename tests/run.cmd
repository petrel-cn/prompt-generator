@echo off
rem Offline regression tests: compile project sources + TestMain.cs, run assertions.
rem No network access, no writes to %APPDATA%.
setlocal
cd /d "%~dp0"

set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set SRC=..
if not exist bin mkdir bin

"%CSC%" /nologo /target:exe /out:bin\verify.exe ^
  /r:System.dll ^
  /r:System.Core.dll ^
  /r:System.Web.Extensions.dll ^
  /r:System.Security.dll ^
  TestMain.cs ^
  %SRC%\Defaults.cs %SRC%\JsonUtil.cs %SRC%\DeepSeekClient.cs %SRC%\Storage.cs

if errorlevel 1 (
  echo TEST BUILD FAILED
  exit /b 1
)

bin\verify.exe
set RESULT=%errorlevel%
if not "%RESULT%"=="0" echo TEST FAILED
endlocal & exit /b %RESULT%
