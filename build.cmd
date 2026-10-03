@echo off
setlocal
cd /d "%~dp0"
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set OUT=bin\PromptGenerator.exe
set ICON=icon\prompt-generator.ico

rem When the script is started by double-clicking, cmdcmdline holds this script's name,
rem so a build failure would vanish with the closing window. In that case keep the
rem window open; when started from an already open console, do not pause.
set KEEP_OPEN=
rem Use the full path to find.exe so a shell that puts GNU find on PATH cannot break this check.
echo %cmdcmdline% | %SystemRoot%\System32\find.exe /i "%~nx0" >nul && set KEEP_OPEN=1

if not exist bin mkdir bin
if not exist "%ICON%" echo ICON NOT FOUND: %ICON%
if not exist "%ICON%" goto fail

rem /win32icon embeds the icon into the exe (Explorer, taskbar and title bar use it)
"%CSC%" /nologo /target:winexe /optimize+ /win32icon:"%ICON%" /out:%OUT% ^
  /r:System.dll ^
  /r:System.Core.dll ^
  /r:System.Drawing.dll ^
  /r:System.Windows.Forms.dll ^
  /r:System.Security.dll ^
  /r:System.Web.Extensions.dll ^
  Program.cs MainForm.cs ConfigForm.cs ViewForm.cs SaveDialog.cs ^
  DeepSeekClient.cs ImageUtil.cs Storage.cs JsonUtil.cs Defaults.cs

if errorlevel 1 goto fail
echo BUILD OK: %OUT%
endlocal
exit /b 0

:fail
echo.
echo BUILD FAILED: no %OUT% was produced. See the compiler errors above.
if defined KEEP_OPEN pause
endlocal
exit /b 1
