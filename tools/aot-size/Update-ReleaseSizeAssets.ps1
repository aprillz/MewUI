[CmdletBinding()]
param(
    [string] $DataPath = (Join-Path $PSScriptRoot 'release-sizes.json'),
    [switch] $Check
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$imagePath = Join-Path $repoRoot 'docs\assets\nativeaot-size.svg'
$data = Get-Content -LiteralPath $DataPath -Raw | ConvertFrom-Json
$entries = @($data.entries)
$samples = @('Hello World', 'Gallery')
$width = 1600
$height = 720
$left = 70
$right = 30
$top = 70
$bottom = 535
$gap = 70
$maxMiB = [Math]::Ceiling(($entries | Measure-Object executableMiB -Maximum).Maximum)
$plotHeight = $bottom - $top
$sectionWidth = ($width - $left - $right - $gap) / 2
$culture = [Globalization.CultureInfo]::InvariantCulture

function Escape-Xml([string] $Value) { [Security.SecurityElement]::Escape($Value) }
function Format-Size([double] $Value) { $Value.ToString('0.000', $culture) }

$svg = [Text.StringBuilder]::new()
[void]$svg.AppendLine('<?xml version="1.0" encoding="utf-8"?>')
[void]$svg.AppendLine("<svg xmlns=`"http://www.w3.org/2000/svg`" width=`"$width`" height=`"$height`" viewBox=`"0 0 $width $height`" role=`"img`" aria-labelledby=`"title description`">")
[void]$svg.AppendLine('<title id="title">MewUI publish size comparison</title>')
[void]$svg.AppendLine('<desc id="description">Original and compressed NativeAOT executable sizes for Hello World and Gallery across supported platforms and rendering backends.</desc>')
[void]$svg.AppendLine('<rect width="100%" height="100%" fill="#fff"/><g font-family="Segoe UI,Arial,sans-serif" fill="#202020">')
[void]$svg.AppendLine('<text x="800" y="30" text-anchor="middle" font-size="22">MewUI Publish Size Comparison</text>')
for ($tick = 0; $tick -le $maxMiB; $tick++) {
    $y = $bottom - ($tick / $maxMiB * $plotHeight)
    [void]$svg.AppendLine("<line x1=`"$left`" y1=`"$y`" x2=`"$($width - $right)`" y2=`"$y`" stroke=`"#ddd`"/><text x=`"$($left - 12)`" y=`"$($y + 5)`" text-anchor=`"end`" font-size=`"14`">$tick</text>")
}
[void]$svg.AppendLine('<text x="20" y="300" text-anchor="middle" font-size="16" transform="rotate(-90 20 300)">Size (MiB)</text>')
[void]$svg.AppendLine('<rect x="82" y="45" width="18" height="12" fill="#287db2"/><text x="108" y="56" font-size="14">Original</text><rect x="180" y="45" width="18" height="12" fill="#ff7f0e"/><text x="206" y="56" font-size="14">Compressed</text>')
for ($section = 0; $section -lt $samples.Count; $section++) {
    $sample = $samples[$section]
    $items = @($entries | Where-Object sample -eq $sample)
    $sectionLeft = $left + $section * ($sectionWidth + $gap)
    $slotWidth = $sectionWidth / $items.Count
    [void]$svg.AppendLine("<text x=`"$($sectionLeft + $sectionWidth / 2)`" y=`"62`" text-anchor=`"middle`" font-size=`"20`">$(Escape-Xml $sample)</text>")
    if ($section -eq 1) {
        $divider = $sectionLeft - $gap / 2
        [void]$svg.AppendLine("<line x1=`"$divider`" y1=`"40`" x2=`"$divider`" y2=`"$bottom`" stroke=`"#287db2`"/>")
    }
    for ($index = 0; $index -lt $items.Count; $index++) {
        $item = $items[$index]
        $center = $sectionLeft + ($index + 0.5) * $slotWidth
        $barWidth = [Math]::Min(42, $slotWidth * 0.28)
        foreach ($bar in @(@($item.executableMiB, '#287db2', -$barWidth), @($item.compressedMiB, '#ff7f0e', 0))) {
            $barHeight = [double]$bar[0] / $maxMiB * $plotHeight
            $x = $center + [double]$bar[2]
            $y = $bottom - $barHeight
            [void]$svg.AppendLine("<rect x=`"$x`" y=`"$y`" width=`"$barWidth`" height=`"$barHeight`" fill=`"$($bar[1])`"/><text x=`"$($x + $barWidth / 2)`" y=`"$($y - 7)`" text-anchor=`"middle`" font-size=`"12`">$(Format-Size $bar[0])</text>")
        }
        $labelParts = @($item.platformBackend -split ' / ', 2)
        $platformLabel = Escape-Xml $labelParts[0]
        $backendLabel = if ($labelParts.Count -gt 1) { Escape-Xml $labelParts[1] } else { '' }
        [void]$svg.AppendLine("<text x=`"$center`" y=`"$($bottom + 28)`" text-anchor=`"middle`" font-size=`"15`" font-weight=`"600`"><tspan x=`"$center`">$platformLabel</tspan><tspan x=`"$center`" dy=`"20`">$backendLabel</tspan></text>")
    }
}
[void]$svg.AppendLine('</g></svg>')

$outputs = @{$imagePath = $svg.ToString().Replace("`r`n", "`n")}

$stale = @()
foreach ($output in $outputs.GetEnumerator()) {
    $current = if (Test-Path -LiteralPath $output.Key) { Get-Content -LiteralPath $output.Key -Raw } else { $null }
    if ($current -cne $output.Value) {
        $stale += $output.Key
        if (-not $Check) {
            [IO.Directory]::CreateDirectory((Split-Path -Parent $output.Key)) | Out-Null
            [IO.File]::WriteAllText($output.Key, $output.Value, [Text.UTF8Encoding]::new($false))
        }
    }
}
if ($Check -and $stale.Count) { throw "Release size assets are stale: $($stale -join ', ')" }
if ($stale.Count) { Write-Host "Updated release size assets: $($stale -join ', ')" } else { Write-Host 'Release size assets are up to date.' }
