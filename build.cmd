@echo off
setlocal
cd /d "%~dp0"
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set OUT=bin\PromptGenerator.exe
set ICON=icon\prompt-generator.ico
if not exist bin mkdir bin
if not exist "%ICON%" echo ICON NOT FOUND: %ICON%
if not exist "%ICON%" exit /b 1

rem /win32icon embeds the icon into the exe (Explorer, taskbar and title bar use it)
"%CSC%" /nologo /target:winexe /optimize+ /win32icon:"%ICON%" /out:%OUT% ^
  /r:System.dll ^
  /r:System.Core.dll ^
  /r:System.Drawing.dll ^
  /r:System.Windows.Forms.dll ^
  /r:System.Security.dll ^
  /r:System.Web.Extensions.dll ^
  Program.cs MainForm.cs ConfigForm.cs ViewForm.cs SaveDialog.cs ^
  DeepSeekClient.cs Storage.cs JsonUtil.cs Defaults.cs

if errorlevel 1 exit /b 1
echo BUILD OK: %OUT%
endlocal
