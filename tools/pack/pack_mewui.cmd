@echo off
setlocal EnableExtensions

REM Packs all MewUI NuGet packages under .artifacts\nuget.
REM Usage: pack_mewui.cmd [version]

set ROOT=%~dp0..\..
set OUT=%ROOT%\.artifacts\nuget
set VERSION=%~1
set FAILED=0

if not exist "%OUT%" mkdir "%OUT%" >nul 2>nul

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
  meta\MewUI.All\MewUI.All.csproj

for %%P in (%PROJECTS%) do (
  echo Packing %%P ...
  if "%VERSION%"=="" (
    dotnet pack "%ROOT%\%%P" -c Release -o "%OUT%" /p:ContinuousIntegrationBuild=true
  ) else (
    dotnet pack "%ROOT%\%%P" -c Release -o "%OUT%" /p:ContinuousIntegrationBuild=true /p:PackageVersion=%VERSION%
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
