# Waits over SSH for a job the console-session broker is running, prints its output and exits with its
# code. Checks the heartbeat first, so a machine sitting at the lock screen reports that immediately
# instead of after the full timeout.
param(
    [Parameter(Mandatory = $true)][string]$Id,
    [string]$Root = 'C:\mewui-remote-test',
    [int]$TimeoutSeconds = 900,
    [int]$HeartbeatToleranceSeconds = 30
)

$ErrorActionPreference = 'Stop'

$heartbeat = Join-Path $Root 'broker.alive'
if (-not (Test-Path $heartbeat)) {
    Write-Output "BROKER DOWN: no heartbeat at $heartbeat."
    Write-Output "Log on at the physical console; the broker starts with that session."
    exit 98
}

$state = Get-Content $heartbeat -Raw | ConvertFrom-Json
$age = ((Get-Date).ToUniversalTime() - [datetime]::Parse($state.Utc).ToUniversalTime()).TotalSeconds
if ($age -gt $HeartbeatToleranceSeconds) {
    Write-Output ("BROKER STALE: last heartbeat {0:0} s ago (user={1} session={2})." -f $age, $state.User, $state.SessionName)
    exit 98
}

$log = Join-Path $Root "results\$Id.log"
$done = Join-Path $Root "results\$Id.done"

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while (-not (Test-Path $done)) {
    if ((Get-Date) -gt $deadline) {
        Write-Output "TIMEOUT: job $Id produced no result within $TimeoutSeconds s."
        if (Test-Path $log) { Get-Content $log }
        exit 99
    }

    Start-Sleep -Seconds 2
}

if (Test-Path $log) { Get-Content $log }

$code = Get-Content $done -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $code -or "$code".Trim() -eq '') {
    Write-Output "BROKER RECORDED NO EXIT CODE for $Id; treat the run above as unverified."
    exit 97
}

exit [int]("$code".Trim())
