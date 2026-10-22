@echo off
setlocal EnableExtensions EnableDelayedExpansion

REM Generates the API reference Markdown for every package assembly (xmldocmd + fluent cross-links)
REM under .artifacts\api-build, then stages the assemblies listed in publish-scope.json under
REM .artifacts\api with the package table and version stamp. Nothing here is committed.
REM Usage: build_api_docs.cmd [ProjectName ...]   (e.g. build_api_docs.cmd MewUI MewUI.Markdown)
REM Requires: dotnet tool restore in this folder (the manifest pins xmldocmd).

set ROOT=%~dp0..\..
set BUILD_OUT=%ROOT%\.artifacts\api-build
set PUBLISH_OUT=%ROOT%\.artifacts\api
set SCOPE_FILE=%~dp0publish-scope.json
set AUDIT_PROJECT=%ROOT%\tools\MewUI.MarkupAudit\MewUI.MarkupAudit\MewUI.MarkupAudit.csproj
set AUDIT_DLL=%ROOT%\tools\MewUI.MarkupAudit\MewUI.MarkupAudit\bin\Release\net10.0\MewUI.MarkupAudit.dll
set TFM=net10.0
set FILTER=%*
set FAILED=0
set DLLS=
set PUBLISH_DLLS=

REM xmldocmd 2.9.0 ships for an older runtime; rolling forward lets it load net10.0 assemblies.
set DOTNET_ROLL_FORWARD=LatestMajor

if not exist "%BUILD_OUT%" mkdir "%BUILD_OUT%" >nul 2>nul

REM --- Package projects that ship an assembly, in pack_mewui.cmd order (metapackages excluded) ---
set PROJECTS=^
  src\MewUI\MewUI.csproj^
  src\MewUI.Platform.Win32\MewUI.Platform.Win32.csproj^
  src\MewUI.Platform.X11\MewUI.Platform.X11.csproj^
  src\MewUI.Platform.MacOS\MewUI.Platform.MacOS.csproj^
  src\MewUI.Backend.Direct2D\MewUI.Backend.Direct2D.csproj^
  src\MewUI.Backend.Gdi\MewUI.Backend.Gdi.csproj^
  src\MewUI.Backend.MewVG.Win32\MewUI.Backend.MewVG.Win32.csproj^
  src\MewUI.Backend.MewVG.X11\MewUI.Backend.MewVG.X11.csproj^
  src\MewUI.Backend.MewVG.MacOS\MewUI.Backend.MewVG.MacOS.csproj^
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

REM --- Version stamp: MewUIVersion from the common props plus the checked-out commit ---
set MEWUI_VERSION=
set COMMIT=
for /f "tokens=3 delims=<>" %%V in ('findstr /c:"<MewUIVersion>" "%ROOT%\build\MewUI.Common.props"') do set MEWUI_VERSION=%%V
for /f "delims=" %%C in ('git -C "%ROOT%" rev-parse HEAD') do set COMMIT=%%C
> "%BUILD_OUT%\version.txt" echo MewUIVersion=%MEWUI_VERSION%
>> "%BUILD_OUT%\version.txt" echo Commit=%COMMIT%

echo Building MewUI.MarkupAudit ...
dotnet build "%AUDIT_PROJECT%" -c Release /nr:false
if errorlevel 1 (
  echo FAILED: MewUI.MarkupAudit build
  exit /b 1
)

REM The publish scope decides which xmldocmd failures are fatal; assemblies outside it only warn.
set SCOPE=
for /f "usebackq delims=" %%S in (`powershell -NoProfile -ExecutionPolicy Bypass -Command "$items = Get-Content -Raw '%SCOPE_FILE%' | ConvertFrom-Json; if ($items) { $items -join ' ' }"`) do set SCOPE=%%S

for %%P in (%PROJECTS%) do call :generate "%%P"

if "!DLLS!"=="" (
  echo No assembly was generated.
  exit /b 1
)

echo Cross-linking fluent extensions ...
dotnet "%AUDIT_DLL%" --api-crosslink "%BUILD_OUT%" !DLLS!
if errorlevel 1 (
  echo FAILED: api-crosslink
  exit /b 1
)

echo Writing index pages ...
dotnet "%AUDIT_DLL%" --api-index "%BUILD_OUT%" !DLLS!
if errorlevel 1 (
  echo FAILED: api-index
  exit /b 1
)

REM --- Publish scope: only the assemblies listed in publish-scope.json are staged under .artifacts\api ---

if exist "%PUBLISH_OUT%" rmdir /s /q "%PUBLISH_OUT%"
mkdir "%PUBLISH_OUT%" >nul 2>nul
copy /y "%BUILD_OUT%\version.txt" "%PUBLISH_OUT%\version.txt" >nul

for %%S in (%SCOPE%) do (
  if defined DLL_%%S (
    echo Publishing %%S ...
    xcopy /e /i /q /y "%BUILD_OUT%\%%S" "%PUBLISH_OUT%\%%S" >nul
    set PUBLISH_DLLS=!PUBLISH_DLLS! "!DLL_%%S!"
  ) else (
    echo SKIPPED: publish-scope.json lists %%S but no pages were generated for it
  )
)

if "!PUBLISH_DLLS!"=="" (
  echo publish-scope.json lists nothing that was generated; %PUBLISH_OUT% holds only version.txt
) else (
  dotnet "%AUDIT_DLL%" --api-index "%PUBLISH_OUT%" !PUBLISH_DLLS!
  if errorlevel 1 (
    echo FAILED: api-index of the publish set
    exit /b 1
  )
)

if %FAILED%==1 (
  echo.
  echo Some packages failed to build or generate.
  exit /b 1
)

echo.
echo API reference generated under %BUILD_OUT%; publish set staged under %PUBLISH_OUT%
exit /b 0

:generate
set PROJECT=%~1
for %%N in ("%PROJECT%") do set NAME=%%~nN
if not "%FILTER%"=="" (
  set MATCH=0
  for %%F in (%FILTER%) do if /i "%%F"=="!NAME!" set MATCH=1
  if "!MATCH!"=="0" exit /b 0
)

set PROJECT_TFM=%TFM%
if "%NAME%"=="MewUI.WebView2.Win32" set PROJECT_TFM=net10.0-windows
set ASSEMBLY=Aprillz.%NAME%
for %%D in ("%ROOT%\%PROJECT%") do set PROJECT_DIR=%%~dpD
set DLL=%PROJECT_DIR%bin\Release\%PROJECT_TFM%\%ASSEMBLY%.dll

echo Building %NAME% (%PROJECT_TFM%) ...
dotnet build "%ROOT%\%PROJECT%" -c Release -f %PROJECT_TFM% -p:CopyLocalLockFileAssemblies=true /nr:false
if errorlevel 1 (
  echo FAILED: %NAME% build
  set FAILED=1
  exit /b 0
)

if not exist "%DLL%" (
  echo SKIPPED: %DLL% not found
  exit /b 0
)

echo Generating %ASSEMBLY% ...
pushd "%~dp0"
dotnet xmldocmd "%DLL%" "%BUILD_OUT%\%ASSEMBLY%" --visibility public --newline lf --clean --quiet
set XMLDOCMD_EXIT=!errorlevel!
popd
if not "!XMLDOCMD_EXIT!"=="0" (
  set IN_SCOPE=0
  for %%S in (%SCOPE%) do if /i "%%S"=="%ASSEMBLY%" set IN_SCOPE=1
  if "!IN_SCOPE!"=="1" (
    echo FAILED: xmldocmd %ASSEMBLY%
    set FAILED=1
  ) else (
    echo WARNING: xmldocmd %ASSEMBLY% failed; the assembly is outside publish-scope.json, so it is skipped
  )
  exit /b 0
)

set DLLS=!DLLS! "%DLL%"
set "DLL_%ASSEMBLY%=%DLL%"
exit /b 0
