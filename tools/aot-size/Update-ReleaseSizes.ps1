[CmdletBinding()]
param(
    [string] $WslDistribution,
    [string] $WslRepo,
    [Parameter(Mandatory = $false)]
    [string] $MacHost,
    [int] $MacPort = 22,
    [string] $MacUser = [Environment]::UserName,
    [string] $MacRepo,
    [string] $MacDotNet = '/usr/local/share/dotnet/dotnet',
    [string] $MacSandbox,
    [switch] $SkipWindows,
    [switch] $SkipLinux,
    [switch] $SkipMacOS,
    [switch] $AllowSdkMismatch
)

$ErrorActionPreference = 'Stop'
if ($SkipWindows -and $SkipLinux -and $SkipMacOS) {
    throw 'At least one platform must be measured.'
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$toolProject = Join-Path $PSScriptRoot 'MewUI.ReleaseSizeTool\MewUI.ReleaseSizeTool.csproj'
$artifactRoot = Join-Path $repoRoot '.artifacts\release-size'
$reportRoot = Join-Path $artifactRoot 'reports'
$dataPath = Join-Path $PSScriptRoot 'release-sizes.json'
[IO.Directory]::CreateDirectory($reportRoot) | Out-Null

function Invoke-Checked {
    param([string] $FilePath, [string[]] $Arguments)

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "'$FilePath' exited with code $LASTEXITCODE."
    }
}

function Invoke-Captured {
    param([string] $FilePath, [string[]] $Arguments)

    $output = & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "'$FilePath' exited with code $LASTEXITCODE."
    }
    return ($output | Select-Object -Last 1).Trim()
}

function Convert-ToPosixPath([string] $Path) {
    $full = [IO.Path]::GetFullPath($Path)
    if ($full -notmatch '^([A-Za-z]):\\(.*)$') {
        throw "Cannot convert '$Path' to a WSL path."
    }
    return "/mnt/$($Matches[1].ToLowerInvariant())/$($Matches[2].Replace('\', '/'))"
}

function Quote-Sh([string] $Value) {
    if ($Value.Contains("'")) {
        throw "Shell arguments containing a single quote are not supported: $Value"
    }
    return "'$Value'"
}

if ([string]::IsNullOrWhiteSpace($WslRepo)) {
    $WslRepo = Convert-ToPosixPath $repoRoot
}
if (-not $SkipMacOS) {
    if ([string]::IsNullOrWhiteSpace($MacHost)) {
        throw '-MacHost is required unless -SkipMacOS is specified.'
    }
    if ([string]::IsNullOrWhiteSpace($MacRepo)) {
        $MacRepo = "/Users/$MacUser/Dev/MewUI"
    }
    if ([string]::IsNullOrWhiteSpace($MacSandbox)) {
        $MacSandbox = "/Users/$MacUser/Sandbox/mewui-release-size"
    }
}

function Invoke-WslCaptured([string] $Command) {
    $arguments = @()
    if (-not [string]::IsNullOrWhiteSpace($WslDistribution)) {
        $arguments += @('--distribution', $WslDistribution)
    }
    $arguments += @('--', 'sh', '-lc', $Command)
    return Invoke-Captured wsl.exe $arguments
}

function Invoke-WslChecked([string] $Command) {
    $arguments = @()
    if (-not [string]::IsNullOrWhiteSpace($WslDistribution)) {
        $arguments += @('--distribution', $WslDistribution)
    }
    $arguments += @('--', 'sh', '-lc', $Command)
    Invoke-Checked wsl.exe $arguments
}

function Invoke-MacCaptured([string] $Command) {
    return Invoke-Captured ssh @('-p', $MacPort, "$MacUser@$MacHost", $Command)
}

function Invoke-MacChecked([string] $Command) {
    Invoke-Checked ssh @('-p', $MacPort, "$MacUser@$MacHost", $Command)
}

$localManifest = Invoke-Captured dotnet @(
    'run', '--project', $toolProject, '-c', 'Release', '--',
    '--repo', $repoRoot, '--manifest-only')
$localSdk = Invoke-Captured dotnet @('--version')
$commonProps = [xml](Get-Content (Join-Path $repoRoot 'build\MewUI.Common.props') -Raw)
$version = $commonProps.SelectSingleNode('/Project/PropertyGroup/MewUIVersion').InnerText.Trim()
$preflight = [Collections.Generic.List[object]]::new()

if (-not $SkipWindows) {
    $preflight.Add([pscustomobject]@{ Platform = 'Windows'; Manifest = $localManifest; Sdk = $localSdk })
}

if (-not $SkipLinux) {
    $linuxTool = "$WslRepo/tools/aot-size/MewUI.ReleaseSizeTool/MewUI.ReleaseSizeTool.csproj"
    $linuxManifest = Invoke-WslCaptured "dotnet run --project $(Quote-Sh $linuxTool) -c Release -- --repo $(Quote-Sh $WslRepo) --manifest-only"
    $linuxSdk = Invoke-WslCaptured 'dotnet --version'
    $preflight.Add([pscustomobject]@{ Platform = 'Linux'; Manifest = $linuxManifest; Sdk = $linuxSdk })
}

if (-not $SkipMacOS) {
    $macTool = "$MacRepo/tools/aot-size/MewUI.ReleaseSizeTool/MewUI.ReleaseSizeTool.csproj"
    $macManifest = Invoke-MacCaptured "$MacDotNet run --project $(Quote-Sh $macTool) -c Release -- --repo $(Quote-Sh $MacRepo) --manifest-only"
    $macSdk = Invoke-MacCaptured "$MacDotNet --version"
    $preflight.Add([pscustomobject]@{ Platform = 'macOS'; Manifest = $macManifest; Sdk = $macSdk })
}

$badManifest = @($preflight | Where-Object Manifest -ne $localManifest)
if ($badManifest.Count -ne 0) {
    throw "Synchronized source differs on: $($badManifest.Platform -join ', '). Synchronize MewUI and retry."
}

$sdkVersions = @($preflight.Sdk | Sort-Object -Unique)
if ($sdkVersions.Count -ne 1 -and -not $AllowSdkMismatch) {
    $sdkSummary = ($preflight | ForEach-Object { "$($_.Platform)=$($_.Sdk)" }) -join ', '
    throw "The measurement SDKs differ: $sdkSummary. Install matching SDKs or explicitly pass -AllowSdkMismatch."
}

$reports = [Collections.Generic.List[string]]::new()
if (-not $SkipWindows) {
    $report = Join-Path $reportRoot 'windows.json'
    Invoke-Checked dotnet @(
        'run', '--project', $toolProject, '-c', 'Release', '--',
        '--repo', $repoRoot,
        '--output', (Join-Path $artifactRoot 'windows'),
        '--report', $report)
    $reports.Add($report)
}

if (-not $SkipLinux) {
    $linuxArtifactRoot = Convert-ToPosixPath (Join-Path $artifactRoot 'linux')
    $linuxReport = Convert-ToPosixPath (Join-Path $reportRoot 'linux.json')
    $linuxTool = "$WslRepo/tools/aot-size/MewUI.ReleaseSizeTool/MewUI.ReleaseSizeTool.csproj"
    Invoke-WslChecked "dotnet run --project $(Quote-Sh $linuxTool) -c Release -- --repo $(Quote-Sh $WslRepo) --output $(Quote-Sh $linuxArtifactRoot) --report $(Quote-Sh $linuxReport)"
    $reports.Add((Join-Path $reportRoot 'linux.json'))
}

if (-not $SkipMacOS) {
    $remoteRoot = $MacSandbox
    $remoteReport = "$remoteRoot/report.json"
    $macTool = "$MacRepo/tools/aot-size/MewUI.ReleaseSizeTool/MewUI.ReleaseSizeTool.csproj"
    Invoke-MacChecked "mkdir -p $(Quote-Sh $remoteRoot) && $(Quote-Sh $MacDotNet) run --project $(Quote-Sh $macTool) -c Release -- --repo $(Quote-Sh $MacRepo) --output $(Quote-Sh "$remoteRoot/output") --report $(Quote-Sh $remoteReport)"
    $localMacReport = Join-Path $reportRoot 'macos.json'
    Invoke-Checked scp @('-P', $MacPort, "$MacUser@$MacHost`:$remoteReport", $localMacReport)
    $reports.Add($localMacReport)
}

$platformReports = @($reports | ForEach-Object { Get-Content $_ -Raw | ConvertFrom-Json })
$measuredEntries = @($platformReports.Entries | ForEach-Object {
    [ordered]@{
        sample = $_.Sample
        platformBackend = $_.PlatformBackend
        executableBytes = [long]$_.ExecutableBytes
        compressedBytes = [long]$_.CompressedBytes
    }
})
$existing = if (Test-Path $dataPath) { Get-Content $dataPath -Raw | ConvertFrom-Json } else { $null }
$measuredKeys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($entry in $measuredEntries) {
    [void]$measuredKeys.Add("$($entry.sample)|$($entry.platformBackend)")
}
$retainedEntries = @()
if ($null -ne $existing) {
    $retainedEntries = @($existing.entries | Where-Object {
        -not $measuredKeys.Contains("$($_.sample)|$($_.platformBackend)")
    } | ForEach-Object {
        $retained = [ordered]@{
            sample = $_.sample
            platformBackend = $_.platformBackend
        }
        if ($null -ne $_.executableBytes) {
            $retained.executableBytes = [long]$_.executableBytes
            $retained.compressedBytes = [long]$_.compressedBytes
        } else {
            $retained.executableMiB = [double]$_.executableMiB
            $retained.compressedMiB = [double]$_.compressedMiB
        }
        $retained
    })
}
$entries = @($retainedEntries) + @($measuredEntries)
$sampleOrder = @{ 'Hello World' = 0; 'Gallery' = 1 }
$platformOrder = @{
    'Windows x64 / GDI' = 0
    'Windows x64 / Direct2D' = 1
    'Windows x64 / MewVG' = 2
    'macOS arm64 / MewVG' = 3
    'Linux x64 / X11 + MewVG' = 4
}
$entries = @($entries | Sort-Object { $sampleOrder[$_.sample] }, { $platformOrder[$_.platformBackend] })

$retainedPlatforms = if ($null -ne $existing) { @($existing.platforms | Where-Object { $null -ne $_ }) } else { @() }
$measuredRids = @($platformReports.RuntimeIdentifier)
$platforms = @($retainedPlatforms | Where-Object runtimeIdentifier -notin $measuredRids) + @($platformReports | ForEach-Object {
    [ordered]@{
        runtimeIdentifier = $_.RuntimeIdentifier
        dotnetSdk = $_.DotnetSdk
        measuredAtUtc = ([DateTime]$_.MeasuredAtUtc).ToUniversalTime().ToString('O')
        sourceManifest = $_.SourceManifest
    }
})
$result = [ordered]@{
    schemaVersion = 2
    mewUIVersion = $version
    generatedAtUtc = [DateTime]::UtcNow.ToString('O')
    platforms = $platforms
    entries = $entries
}
$json = $result | ConvertTo-Json -Depth 6
[IO.File]::WriteAllText($dataPath, $json + "`n", [Text.UTF8Encoding]::new($false))

& (Join-Path $PSScriptRoot 'Update-ReleaseSizeAssets.ps1')
if ($LASTEXITCODE -ne 0) {
    throw 'Release size asset generation failed.'
}

Write-Host "Updated MewUI v$version release sizes: $dataPath"
