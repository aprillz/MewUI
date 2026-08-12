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
    [ValidateSet(1, 3)]
    [int] $StartAt = 1,
    [switch] $SkipWindows,
    [switch] $SkipLinux,
    [switch] $SkipMacOS
)

$ErrorActionPreference = 'Stop'
if ($SkipWindows -and $SkipLinux -and $SkipMacOS) {
    throw 'At least one platform must be measured.'
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$toolProject = Join-Path $PSScriptRoot 'MewUI.ReleaseSizeTool\MewUI.ReleaseSizeTool.csproj'
$artifactRoot = Join-Path $repoRoot '.artifacts\release-size'
$reportRoot = Join-Path $artifactRoot 'reports'
$windowsToolArtifacts = Join-Path $artifactRoot 'tool\windows'
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
$linuxToolArtifacts = "$WslRepo/.artifacts/release-size/tool/linux"
if ($StartAt -ne 3 -and -not $SkipMacOS) {
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

$commonProps = [xml](Get-Content (Join-Path $repoRoot 'build\MewUI.Common.props') -Raw)
$version = $commonProps.SelectSingleNode('/Project/PropertyGroup/MewUIVersion').InnerText.Trim()
$reports = [Collections.Generic.List[string]]::new()

if ($StartAt -eq 3) {
    Write-Host '[1/3] Skipped source synchronization check'
    Write-Host '[2/3] Reusing existing measurement reports'
    $selectedReports = @()
    if (-not $SkipWindows) { $selectedReports += [pscustomobject]@{ Platform = 'Windows'; Rid = 'win-x64'; File = 'windows.json' } }
    if (-not $SkipLinux) { $selectedReports += [pscustomobject]@{ Platform = 'Linux'; Rid = 'linux-x64'; File = 'linux.json' } }
    if (-not $SkipMacOS) { $selectedReports += [pscustomobject]@{ Platform = 'macOS'; Rid = 'osx-arm64'; File = 'macos.json' } }

    foreach ($selected in $selectedReports) {
        $report = Join-Path $reportRoot $selected.File
        if (-not (Test-Path -LiteralPath $report)) {
            throw "$($selected.Platform) report was not found: $report. Run measurement first or skip that platform."
        }
        $reportData = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
        if ($reportData.RuntimeIdentifier -ne $selected.Rid) {
            throw "$($selected.Platform) report has runtime identifier '$($reportData.RuntimeIdentifier)', expected '$($selected.Rid)'."
        }
        if ($reportData.MewUIVersion -ne $version) {
            throw "$($selected.Platform) report is for MewUI v$($reportData.MewUIVersion), expected v$version."
        }
        $reports.Add($report)
        Write-Host "  $($selected.Platform): $($selected.File)"
    }
} else {
    Write-Host '[1/3] Checking synchronized sources'
    Write-Host '  Windows: computing manifest...'
    $localManifest = Invoke-Captured dotnet @(
        'run', '--project', $toolProject, '-c', 'Release',
        '--artifacts-path', $windowsToolArtifacts, '--',
        '--repo', $repoRoot, '--manifest-only')
    $preflight = [Collections.Generic.List[object]]::new()

    if (-not $SkipWindows) {
        $preflight.Add([pscustomobject]@{ Platform = 'Windows'; Manifest = $localManifest })
        Write-Host '  Windows: ready'
    }

    if (-not $SkipLinux) {
        Write-Host '  Linux: computing manifest through WSL...'
        $linuxTool = "$WslRepo/tools/aot-size/MewUI.ReleaseSizeTool/MewUI.ReleaseSizeTool.csproj"
        $linuxManifest = Invoke-WslCaptured "dotnet run --project $(Quote-Sh $linuxTool) -c Release --artifacts-path $(Quote-Sh $linuxToolArtifacts) -- --repo $(Quote-Sh $WslRepo) --manifest-only"
        $preflight.Add([pscustomobject]@{ Platform = 'Linux'; Manifest = $linuxManifest })
        Write-Host '  Linux: ready'
    }

    if (-not $SkipMacOS) {
        Write-Host '  macOS: computing manifest through SSH...'
        $macTool = "$MacRepo/tools/aot-size/MewUI.ReleaseSizeTool/MewUI.ReleaseSizeTool.csproj"
        $macManifest = Invoke-MacCaptured "$(Quote-Sh $MacDotNet) run --project $(Quote-Sh $macTool) -c Release --artifacts-path $(Quote-Sh "$MacSandbox/tool") -- --repo $(Quote-Sh $MacRepo) --manifest-only"
        $preflight.Add([pscustomobject]@{ Platform = 'macOS'; Manifest = $macManifest })
        Write-Host '  macOS: ready'
    }

    $badManifest = @($preflight | Where-Object Manifest -ne $localManifest)
    if ($badManifest.Count -ne 0) {
        throw "Synchronized source differs on: $($badManifest.Platform -join ', '). Synchronize MewUI and retry."
    }
    Write-Host '  Source manifests match.'

    $platformCount = @($preflight).Count
    $platformIndex = 0
    Write-Host '[2/3] Measuring NativeAOT executables'
    if (-not $SkipWindows) {
        $platformIndex++
        Write-Host "Platform [$platformIndex/$platformCount] Windows"
        $report = Join-Path $reportRoot 'windows.json'
        Invoke-Checked dotnet @(
            'run', '--project', $toolProject, '-c', 'Release',
            '--artifacts-path', $windowsToolArtifacts, '--',
            '--repo', $repoRoot,
            '--output', (Join-Path $artifactRoot 'windows'),
            '--report', $report)
        $reports.Add($report)
        Write-Host "Platform [$platformIndex/$platformCount] Windows complete"
    }

    if (-not $SkipLinux) {
        $platformIndex++
        Write-Host "Platform [$platformIndex/$platformCount] Linux"
        $linuxArtifactRoot = Convert-ToPosixPath (Join-Path $artifactRoot 'linux')
        $linuxReport = Convert-ToPosixPath (Join-Path $reportRoot 'linux.json')
        $linuxTool = "$WslRepo/tools/aot-size/MewUI.ReleaseSizeTool/MewUI.ReleaseSizeTool.csproj"
        Invoke-WslChecked "dotnet run --project $(Quote-Sh $linuxTool) -c Release --artifacts-path $(Quote-Sh $linuxToolArtifacts) -- --repo $(Quote-Sh $WslRepo) --output $(Quote-Sh $linuxArtifactRoot) --report $(Quote-Sh $linuxReport)"
        $reports.Add((Join-Path $reportRoot 'linux.json'))
        Write-Host "Platform [$platformIndex/$platformCount] Linux complete"
    }

    if (-not $SkipMacOS) {
        $platformIndex++
        Write-Host "Platform [$platformIndex/$platformCount] macOS"
        $remoteRoot = $MacSandbox
        $remoteReport = "$remoteRoot/report.json"
        $macTool = "$MacRepo/tools/aot-size/MewUI.ReleaseSizeTool/MewUI.ReleaseSizeTool.csproj"
        Invoke-MacChecked "mkdir -p $(Quote-Sh $remoteRoot) && $(Quote-Sh $MacDotNet) run --project $(Quote-Sh $macTool) -c Release --artifacts-path $(Quote-Sh "$remoteRoot/tool") -- --repo $(Quote-Sh $MacRepo) --output $(Quote-Sh "$remoteRoot/output") --report $(Quote-Sh $remoteReport) --dotnet $(Quote-Sh $MacDotNet)"
        $localMacReport = Join-Path $reportRoot 'macos.json'
        Invoke-Checked scp @('-P', $MacPort, "$MacUser@$MacHost`:$remoteReport", $localMacReport)
        $reports.Add($localMacReport)
        Write-Host "Platform [$platformIndex/$platformCount] macOS complete"
    }
}

Write-Host '[3/3] Updating measurement data and SVG assets'
$platformReports = @($reports | ForEach-Object { Get-Content $_ -Raw | ConvertFrom-Json })
$reportManifests = @($platformReports.SourceManifest | Sort-Object -Unique)
if ($reportManifests.Count -ne 1) {
    throw 'Selected reports were measured from different source manifests.'
}
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
