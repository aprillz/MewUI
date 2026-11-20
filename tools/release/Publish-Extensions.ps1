<#
.SYNOPSIS
    Packages and publishes the MewUI Preview editor extensions.

.DESCRIPTION
    Builds the VS Code extension (vsce) and the Visual Studio extension (VSSDK), drops both packages
    under .artifacts/extensions, and publishes them to their stores. Both stores sit under the same
    marketplace publisher and take the same token; only the package format and the upload tool
    differ, so each half runs on its own and a failure on one side leaves the other package on disk
    to publish by hand.

    Publishing is skipped entirely with -PackageOnly, which is what a dry run before a release does.

.PARAMETER Target
    Which extension to handle: vscode, vs, or all (default).

.PARAMETER Version
    Expected version, without a leading v. When given, the run stops unless both manifests already
    declare it. The extensions version independently of MewUI, so this is a check, not a bump.

.PARAMETER PackageOnly
    Builds the packages and stops before publishing.

.PARAMETER Token
    Azure DevOps personal access token (Marketplace: Manage). One token covers both stores, since
    they share the publisher account. Falls back to $env:MARKETPLACE_PAT, then to a single prompt.

.EXAMPLE
    ./tools/release/Publish-Extensions.ps1 -PackageOnly

.EXAMPLE
    ./tools/release/Publish-Extensions.ps1 -Target vscode -Version 0.1.0
#>
[CmdletBinding()]
param(
    [ValidateSet('vscode', 'vs', 'all')]
    [string] $Target = 'all',

    [string] $Version,

    [switch] $PackageOnly,

    [string] $Token
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ReleaseCommon.ps1')

$vscodeDir = Join-Path $script:RepoRoot 'tools\vscode-mewui'
$vsDir = Join-Path $script:RepoRoot 'tools\MewUI.VisualStudio'
$outputDir = Join-Path $script:RepoRoot '.artifacts\extensions'

function Get-VsCodeManifest {
    return Get-Content -LiteralPath (Join-Path $vscodeDir 'package.json') -Raw | ConvertFrom-Json
}

function Get-VsCodeVersion {
    return (Get-VsCodeManifest).version
}

function Get-VsixVersion {
    $manifest = [xml](Get-Content -LiteralPath (Join-Path $vsDir 'source.extension.vsixmanifest') -Raw)
    return $manifest.PackageManifest.Metadata.Identity.Version
}

function Get-Token {
    if ($script:ResolvedToken) { return $script:ResolvedToken }

    if ($Token) {
        $script:ResolvedToken = $Token
    } else {
        $fromEnvironment = [Environment]::GetEnvironmentVariable('MARKETPLACE_PAT')
        if ($fromEnvironment) {
            $script:ResolvedToken = $fromEnvironment
        } else {
            $secure = Read-Host -Prompt 'Marketplace token' -AsSecureString
            $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
            try {
                $script:ResolvedToken = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
            } finally {
                [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
            }
        }
    }

    return $script:ResolvedToken
}

function Find-MSBuild {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere)) {
        throw "vswhere.exe is missing, so the VS extension cannot be built here."
    }

    # -prerelease: VS 2026 previews are what this extension is verified against.
    $found = & $vswhere -latest -prerelease -products * -requires Microsoft.Component.MSBuild `
        -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
    if (-not $found) {
        throw "No MSBuild was found. The VS extension needs .NET Framework MSBuild from a VS install."
    }
    return $found
}

function Find-VsixPublisher {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere)) { return $null }

    return (& $vswhere -latest -prerelease -products * `
        -find 'VSSDK\VisualStudioIntegration\Tools\Bin\VsixPublisher.exe' | Select-Object -First 1)
}

if ($Version) {
    Write-Step "Checking the declared versions"

    $vscodeVersion = Get-VsCodeVersion
    $vsixVersion = Get-VsixVersion
    Write-Host "  vscode-mewui $vscodeVersion"
    Write-Host "  MewUI.VisualStudio $vsixVersion"

    if ($Target -ne 'vs' -and $vscodeVersion -ne $Version) {
        throw "tools/vscode-mewui/package.json declares $vscodeVersion, not $Version."
    }
    if ($Target -ne 'vscode' -and $vsixVersion -ne $Version) {
        throw "source.extension.vsixmanifest declares $vsixVersion, not $Version."
    }
}

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

if ($Target -eq 'vscode' -or $Target -eq 'all') {
    Write-Step "Packaging the VS Code extension"

    Push-Location $vscodeDir
    try {
        if (-not (Test-Path -LiteralPath (Join-Path $vscodeDir 'node_modules'))) {
            & npm ci
            if ($LASTEXITCODE -ne 0) { throw "npm ci failed with exit code $LASTEXITCODE." }
        }

        $vsix = Join-Path $outputDir "$((Get-VsCodeManifest).name)-$(Get-VsCodeVersion).vsix"
        & npx --no-install vsce package --out $vsix
        if ($LASTEXITCODE -ne 0) { throw "vsce package failed with exit code $LASTEXITCODE." }
        Write-Host "  $vsix"

        if (-not $PackageOnly) {
            Write-Step "Publishing to the VS Code marketplace"

            $token = Get-Token
            & npx --no-install vsce publish --packagePath $vsix --pat $token
            if ($LASTEXITCODE -ne 0) { throw "vsce publish failed with exit code $LASTEXITCODE." }
        }
    } finally {
        Pop-Location
    }
}

if ($Target -eq 'vs' -or $Target -eq 'all') {
    Write-Step "Building the Visual Studio extension"

    $msbuild = Find-MSBuild
    Write-Host "  $msbuild"

    & $msbuild (Join-Path $vsDir 'MewUI.VisualStudio.csproj') -restore -p:Configuration=Release -v:m
    if ($LASTEXITCODE -ne 0) { throw "The VSIX build failed with exit code $LASTEXITCODE." }

    $built = Join-Path $vsDir 'bin\Release\MewUI.VisualStudio.vsix'
    $vsix = Join-Path $outputDir "MewUI.VisualStudio-$(Get-VsixVersion).vsix"
    Copy-Item -LiteralPath $built -Destination $vsix -Force
    Write-Host "  $vsix"

    if (-not $PackageOnly) {
        Write-Step "Publishing to the Visual Studio marketplace"

        $publisher = Find-VsixPublisher
        if (-not $publisher) {
            # VsixPublisher ships with the VS extension development workload, which this repository
            # does not require to build the VSIX. Uploading the same package by hand is equivalent.
            Write-Host "  VsixPublisher.exe was not found (the VS extension development workload is not installed)."
            Write-Host "  Upload $vsix at https://marketplace.visualstudio.com/manage/publishers/aprillz instead."
        } else {
            $token = Get-Token
            & $publisher publish -payload $vsix `
                -publishManifest (Join-Path $vsDir 'publishManifest.json') -personalAccessToken $token
            if ($LASTEXITCODE -ne 0) { throw "VsixPublisher failed with exit code $LASTEXITCODE." }
        }
    }
}

Write-Step "Done"
