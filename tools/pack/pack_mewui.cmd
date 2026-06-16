@echo off
setlocal EnableExtensions

REM Packs all MewUI NuGet packages under .artifacts\nuget.
REM Usage: pack_mewui.cmd [version]

set ROOT=%~dp0..\..
set OUT=%ROOT%\.artifacts\nuget
set VERSION=%~1
set FAILED=0

if not exist "%OUT%" mkdir "%OUT%" >nul 2>nul

REM --- Build the analyzer first; it is bundled into Core's analyzers/dotnet/cs ---
echo Building analyzer ...
dotnet build "%ROOT%\src\MewUI.Analyzers\MewUI.Analyzers.csproj" -c Release /nr:false
if errorlevel 1 (
  echo FAILED: analyzer build
  exit /b 1
)

REM --- Individual packages (src) ---
set PROJECTS=^
  src\MewUI\MewUI.csproj^
  src\MewUI.Platform.Win32\MewUI.Platform.Win32.csproj^
  src\MewUI.Platform.X11\MewUI.Platform.X11.csproj^
  src\MewUI.Platform.MacOS\MewUI.Platform.MacOS.csproj^
  src\MewUI.Backend.Direct2D\MewUI.Backend.Direct2D.csproj^
  src\MewUI.Backend.Gdi\MewUI.Backend.Gdi.csproj^
  src\MewUI.Backend.MewVG.Win32\MewUI.Backend.MewVG.Win32.csproj^
  src\MewUI.Backend.MewVG.X11\MewUI.Backend.MewVG.X11.csproj^
  src\MewUI.Backend.MewVG.MacOS\MewUI.Backend.MewVG.MacOS.csproj

REM --- Metapackages (meta) ---
set PROJECTS=%PROJECTS%^
  meta\MewUI.Windows\MewUI.Windows.csproj^
  meta\MewUI.Linux\MewUI.Linux.csproj^
  meta\MewUI.MacOS\MewUI.MacOS.csproj^
  meta\MewUI.All\MewUI.All.csproj^
  meta\MewUI.Skia.Windows\MewUI.Skia.Windows.csproj^
  meta\MewUI.Skia.Linux\MewUI.Skia.Linux.csproj^
  meta\MewUI.Skia.MacOS\MewUI.Skia.MacOS.csproj^
  meta\MewUI.Skia.All\MewUI.Skia.All.csproj

REM --- Extensions (extensions) ---
set PROJECTS=%PROJECTS%^
  extensions\MewUI.MewDock\MewUI.MewDock.csproj^
  extensions\MewUI.Svg\MewUI.Svg.csproj^
  extensions\MewUI.MewCharts\MewUI.MewCharts.csproj^
  extensions\MewUI.WebView2.Win32\MewUI.WebView2.Win32.csproj^
  extensions\MewUI.Skia\MewUI.Skia.csproj^
  extensions\MewUI.Skia.Interop.Direct2D\MewUI.Skia.Interop.Direct2D.csproj^
  extensions\MewUI.Skia.Interop.Gdi\MewUI.Skia.Interop.Gdi.csproj^
  extensions\MewUI.Skia.Interop.MewVG.Win32\MewUI.Skia.Interop.MewVG.Win32.csproj^
  extensions\MewUI.Skia.Interop.MewVG.X11\MewUI.Skia.Interop.MewVG.X11.csproj^
  extensions\MewUI.Skia.Interop.MewVG.MacOS\MewUI.Skia.Interop.MewVG.MacOS.csproj

for %%P in (%PROJECTS%) do (
  echo Packing %%P ...
  if "%VERSION%"=="" (
    dotnet pack "%ROOT%\%%P" -c Release -o "%OUT%" /p:ContinuousIntegrationBuild=true /nr:false
  ) else (
    dotnet pack "%ROOT%\%%P" -c Release -o "%OUT%" /p:ContinuousIntegrationBuild=true /p:PackageVersion=%VERSION% /nr:false
  )
  if errorlevel 1 (
    echo FAILED: %%P
    set FAILED=1
  )
)

if %FAILED%==1 (
  echo.
  echo Some packages failed to pack.
  exit /b 1
)

echo.
echo All packages packed to %OUT%
exit /b 0
