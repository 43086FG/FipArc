@echo off
rem build.cmd          -> 完整版   (目录 build\)
rem build.cmd se       -> 精简版   (目录 build_se\)
setlocal enabledelayedexpansion
cd /d "%~dp0"

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo [ERROR] csc.exe not found
  exit /b 1
)

set WPF=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\WPF
set REFS=/r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:"%WPF%\WindowsBase.dll" /r:"%WPF%\PresentationCore.dll" /r:"%WPF%\PresentationFramework.dll" /r:"%WPF%\WindowsFormsIntegration.dll" /r:System.Xaml.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll
set OPTS=/nologo /optimize+ /codepage:65001 /win32icon:assets\app.ico

set OUT=build
set DEF=
set LABEL=full

if /i "%~1"=="se" (
  set OUT=build_se
  set DEF=/define:SE
  set LABEL=SE
)

if not exist %OUT% mkdir %OUT%

echo === Building %LABEL% edition into %OUT%\ ===

echo === [1/3] Building FipArc.exe (main application) ===
"%CSC%" /target:winexe %OPTS% %DEF% %REFS% /out:%OUT%\FipArc.exe src\*.cs
if errorlevel 1 (
  echo [ERROR] main app build failed
  exit /b 1
)

echo.
echo === [2/3] Building Uninstall.exe (dedicated uninstaller) ===
"%CSC%" /target:winexe %OPTS% %DEF% /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:"%WPF%\WindowsBase.dll" /r:"%WPF%\PresentationCore.dll" /r:"%WPF%\PresentationFramework.dll" /r:"%WPF%\WindowsFormsIntegration.dll" /r:System.Xaml.dll ^
  /main:FipArcSetup.UninstallMain ^
  /out:%OUT%\Uninstall.exe installer\*.cs src\Shell.cs src\Lang.cs
if errorlevel 1 (
  echo [ERROR] uninstaller build failed
  exit /b 1
)

echo === [3/3] Building FipArcSetup.exe (installer) ===
"%CSC%" /target:winexe %OPTS% %DEF% /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:"%WPF%\WindowsBase.dll" /r:"%WPF%\PresentationCore.dll" /r:"%WPF%\PresentationFramework.dll" /r:"%WPF%\WindowsFormsIntegration.dll" /r:System.Xaml.dll ^
  /resource:%OUT%\FipArc.exe,FipArc.exe ^
  /resource:assets\app.ico,app.ico ^
  /resource:vendor\full\7z.exe,7z.exe ^
  /resource:vendor\full\7z.dll,7z.dll ^
  /resource:vendor\full\License.txt,License-7zip.txt ^
  /resource:vendor\lav\lav.zip,LAVFilters.zip ^
  /resource:%OUT%\Uninstall.exe,Uninstall.exe ^
  /main:FipArcSetup.Program /out:%OUT%\FipArcSetup.exe installer\*.cs src\Shell.cs src\Lang.cs
if errorlevel 1 (
  echo [ERROR] setup build failed
  exit /b 1
)

echo.
echo === BUILD OK (%LABEL%) ===
dir /b %OUT%
