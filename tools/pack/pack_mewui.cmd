@echo off
setlocal EnableExtensions EnableDelayedExpansion

REM Packs all MewUI NuGet packages under .artifacts\nuget, then runs the fluent coverage gate
REM over the packed UI assemblies; a gate failure fails the script so the packages are not released.
REM Usage: pack_mewui.cmd [version]

set ROOT=%~dp0..\..
set OUT=%ROOT%\.artifacts\nuget
set VERSION=%~1
set FAILED=0
set AUDIT_PROJECT=%ROOT%\tools\MewUI.MarkupAudit\MewUI.MarkupAudit\MewUI.MarkupAudit.csproj
set AUDIT_DLL=%ROOT%\tools\MewUI.MarkupAudit\MewUI.MarkupAudit\bin\Release\net10.0\MewUI.MarkupAudit.dll
set AUDIT_REPORT=%ROOT%\.artifacts\fluent-audit.md

if not exist "%OUT%" mkdir "%OUT%" >nul 2>nul

REM --- Build the analyzer first; it is bundled into Core's analyzers/dotnet/cs ---
echo Building analyzer ...
dotnet build "%ROOT%\src\MewUI.Analyzers\MewUI.Analyzers.csproj" -c Release /nr:false
if errorlevel 1 (
  echo FAILED: analyzer build
  exit /b 1
)

REM --- Build the generator; it is bundled into Core's analyzers/dotnet/roslyn4.12/cs ---
echo Building generator ...
dotnet build "%ROOT%\src\MewUI.Generators\MewUI.Generators.csproj" -c Release /nr:false
if errorlevel 1 (
  echo FAILED: generator build
  exit /b 1
)

REM --- Build the previewer; Core bundles it and cannot reference the project (it references Core) ---
echo Building previewer ...
dotnet build "%ROOT%\src\MewUI.Preview\MewUI.Preview.csproj" -c Release /nr:false
if errorlevel 1 (
  echo FAILED: previewer build
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
  extensions\MewUI.Geometry\MewUI.Geometry.csproj^
  extensions\MewUI.MewDock\MewUI.MewDock.csproj^
  extensions\MewUI.Svg\MewUI.Svg.csproj^
  extensions\MewUI.MewCharts\MewUI.MewCharts.csproj^
  extensions\MewUI.Markdown\MewUI.Markdown.csproj^
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

REM --- Fluent coverage gate: the Release assemblies exist only after the pack loop, so it runs here ---
REM Every packaged UI assembly is gated (Terminal and MewvalonEdit are not packaged yet, so their
REM stale Release outputs are not audited here). Platform and backend assemblies declare no UI receivers.
set GATE_ASSEMBLIES=^
  src\MewUI\bin\Release\net10.0\Aprillz.MewUI.dll^
  extensions\MewUI.Geometry\bin\Release\net10.0\Aprillz.MewUI.Geometry.dll^
  extensions\MewUI.MewDock\bin\Release\net10.0\Aprillz.MewUI.MewDock.dll^
  extensions\MewUI.Svg\bin\Release\net10.0\Aprillz.MewUI.Svg.dll^
  extensions\MewUI.MewCharts\bin\Release\net10.0\Aprillz.MewUI.MewCharts.dll^
  extensions\MewUI.Markdown\bin\Release\net10.0\Aprillz.MewUI.Markdown.dll^
  extensions\MewUI.WebView2.Win32\bin\Release\net10.0-windows\Aprillz.MewUI.WebView2.Win32.dll^
  extensions\MewUI.Skia\bin\Release\net10.0\Aprillz.MewUI.Skia.dll^
  extensions\MewUI.Skia.Interop.Direct2D\bin\Release\net10.0\Aprillz.MewUI.Skia.Interop.Direct2D.dll^
  extensions\MewUI.Skia.Interop.Gdi\bin\Release\net10.0\Aprillz.MewUI.Skia.Interop.Gdi.dll^
  extensions\MewUI.Skia.Interop.MewVG.Win32\bin\Release\net10.0\Aprillz.MewUI.Skia.Interop.MewVG.Win32.dll^
  extensions\MewUI.Skia.Interop.MewVG.X11\bin\Release\net10.0\Aprillz.MewUI.Skia.Interop.MewVG.X11.dll^
  extensions\MewUI.Skia.Interop.MewVG.MacOS\bin\Release\net10.0\Aprillz.MewUI.Skia.Interop.MewVG.MacOS.dll

echo Building MewUI.MarkupAudit ...
dotnet build "%AUDIT_PROJECT%" -c Release /nr:false
if errorlevel 1 (
  echo FAILED: MewUI.MarkupAudit build
  exit /b 1
)

set GATE_DLLS=
for %%A in (%GATE_ASSEMBLIES%) do (
  if exist "%ROOT%\%%A" (
    set GATE_DLLS=!GATE_DLLS! "%ROOT%\%%A"
  ) else (
    echo Gate: %%A was not built; skipped
  )
)

echo Running fluent coverage gate ...
dotnet "%AUDIT_DLL%" --fail-on-missing !GATE_DLLS! > "%AUDIT_REPORT%"
if errorlevel 1 (
  echo.
  echo FAILED: fluent coverage gate. Packages under %OUT% must not be published; see %AUDIT_REPORT%
  exit /b 1
)

echo.
echo All packages packed to %OUT%
exit /b 0
