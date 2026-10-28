@echo off
setlocal EnableExtensions

REM Creates an app from each template in the packed Aprillz.MewUI.Templates and builds it against the
REM packages beside it, so a template that no longer builds fails the pack instead of reaching users.
REM Usage: check_templates.cmd <package folder> <version>

set FEED=%~f1
set VERSION=%~2
for %%I in ("%~dp0..\..") do set ROOT=%%~fI
set WORK=%ROOT%\.artifacts\template-check
set HIVE=%WORK%\hive
set TEMPLATES=%FEED%\Aprillz.MewUI.Templates.%VERSION%.nupkg

if "%VERSION%"=="" (
  echo Usage: check_templates.cmd ^<package folder^> ^<version^>
  exit /b 1
)
if not exist "%TEMPLATES%" (
  echo FAILED: %TEMPLATES% not found
  exit /b 1
)

REM The browser app compiles a native shim, so its build needs the WebAssembly build tools.
dotnet workload list | findstr /r /c:"^wasm-tools " >nul
if errorlevel 1 (
  echo FAILED: the browser template check needs the wasm-tools workload. Install it with: dotnet workload install wasm-tools
  exit /b 1
)

if exist "%WORK%" rmdir /s /q "%WORK%"
mkdir "%WORK%"

REM A private template store and package folder: the user's installed templates and cached packages of
REM the same version must not stand in for the ones just packed.
(
  echo ^<?xml version="1.0" encoding="utf-8"?^>
  echo ^<configuration^>
  echo   ^<packageSources^>
  echo     ^<clear /^>
  echo     ^<add key="mewui" value="%FEED%" /^>
  echo     ^<add key="nuget.org" value="https://api.nuget.org/v3/index.json" /^>
  echo   ^</packageSources^>
  echo ^</configuration^>
) > "%WORK%\nuget.config"

REM No labels: the repository stores .cmd files with LF line endings, where cmd can miss a goto target.
dotnet new install "%TEMPLATES%" --debug:custom-hive "%HIVE%"
if errorlevel 1 (
  echo FAILED: installing %TEMPLATES%
  exit /b 1
)

for %%T in (mewui mewui-browser) do (
  echo Creating and building %%T ...
  dotnet new %%T -o "%WORK%\%%T" --debug:custom-hive "%HIVE%"
  if errorlevel 1 (
    echo FAILED: dotnet new %%T
    exit /b 1
  )
  dotnet build "%WORK%\%%T" -c Release -p:RestorePackagesPath="%WORK%\packages" /nr:false
  if errorlevel 1 (
    echo FAILED: building the %%T app; it is left under %WORK%\%%T
    exit /b 1
  )
)

rmdir /s /q "%WORK%"
echo Templates built against %VERSION%
exit /b 0
