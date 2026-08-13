# Job broker for the remote GUI test machine. Runs in the physical console session, which is the only
# session that owns the real monitors and their per-monitor scales: SSH lands in the sshd service
# session, where display enumeration reports one default screen and windows never reach a monitor.
#
# Picks up job scripts dropped in jobs\, runs them, and writes each run's output and exit code to
# results\. Publishes a heartbeat so a caller can tell "nobody is logged on" from "still running".
param(
    [string]$Root = 'C:\mewui-remote-test',
    [int]$PollMilliseconds = 500
)

$ErrorActionPreference = 'Stop'

$jobsDir = Join-Path $Root 'jobs'
$runningDir = Join-Path $Root 'running'
$resultsDir = Join-Path $Root 'results'
$heartbeat = Join-Path $Root 'broker.alive'

foreach ($dir in @($jobsDir, $runningDir, $resultsDir)) {
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
}

function Write-Heartbeat {
    # Session name distinguishes the console session from an sshd or RDP one, which is the whole point
    # of running the broker here rather than over the SSH connection.
    $state = [PSCustomObject]@{
        Utc = (Get-Date).ToUniversalTime().ToString('o')
        User = $env:USERNAME
        SessionName = $env:SESSIONNAME
        Pid = $PID
    }

    $state | ConvertTo-Json -Compress | Set-Content -Path $heartbeat -Encoding utf8
}

function Invoke-Job {
    param([string]$JobFile)

    $id = [System.IO.Path]::GetFileNameWithoutExtension($JobFile)
    $running = Join-Path $runningDir ([System.IO.Path]::GetFileName($JobFile))
    Move-Item -Path $JobFile -Destination $running -Force

    $log = Join-Path $resultsDir "$id.log"
    $done = Join-Path $resultsDir "$id.done"

    Write-Output "[$(Get-Date -Format HH:mm:ss)] running $id"

    # Streams are captured by Start-Process rather than by a redirection inside the command string:
    # the string form goes through PowerShell's own quoting and arrives at cmd mangled. Piping the
    # process through PowerShell instead is no good either, because 5.1 turns a native program's stderr
    # into error records and reports failure on exit code 0.
    $outFile = Join-Path $resultsDir "$id.out"
    $errFile = Join-Path $resultsDir "$id.err"
    $exitCode = 1
    try {
        # Not -Wait: the heartbeat has to keep ticking while the job runs, or a suite that takes longer
        # than the caller's staleness tolerance makes a healthy broker look logged off.
        $process = Start-Process -FilePath $env:ComSpec -ArgumentList '/c', $running `
            -NoNewWindow -PassThru -RedirectStandardOutput $outFile -RedirectStandardError $errFile

        # Touching Handle caches it while the process is alive. Without that, ExitCode reads back as
        # null once the process is gone and the run reports no result at all.
        $null = $process.Handle

        while (-not $process.HasExited) {
            Write-Heartbeat
            Start-Sleep -Milliseconds $PollMilliseconds
        }

        $process.WaitForExit()
        $exitCode = $process.ExitCode

        Get-Content $outFile, $errFile -ErrorAction SilentlyContinue | Set-Content -Path $log -Encoding utf8
        Remove-Item $outFile, $errFile -Force -ErrorAction SilentlyContinue
    }
    catch {
        Set-Content -Path $log -Value "broker failed to start the job: $_" -Encoding utf8
    }

    # Plain ASCII, no BOM: this file carries one number and a caller parses it.
    if ($null -eq $exitCode) { $exitCode = -1 }
    Set-Content -Path $done -Value $exitCode -Encoding ascii
    Write-Output "[$(Get-Date -Format HH:mm:ss)] $id exited $exitCode"
}

Write-Output "MewUI remote test broker: root=$Root user=$env:USERNAME session=$env:SESSIONNAME"

# The console session is the one place a person has to be physically present to reach, so the broker
# picks up its own updates instead of asking for a restart at the machine.
$scriptPath = $PSCommandPath
$scriptStamp = (Get-Item $scriptPath).LastWriteTimeUtc

while ($true) {
    # Nothing restarts this process, so a transient failure (a file locked by the SSH upload, a full
    # disk) must not end the loop: it would leave the machine looking logged off to every later run.
    try {
        Write-Heartbeat

        # Between jobs only, and only once the file has stopped changing, so a half-finished upload is
        # never the version that gets launched.
        $stamp = (Get-Item $scriptPath).LastWriteTimeUtc
        if ($stamp -ne $scriptStamp -and ((Get-Date).ToUniversalTime() - $stamp).TotalSeconds -gt 2) {
            Write-Output "[$(Get-Date -Format HH:mm:ss)] broker.ps1 changed; restarting"
            Start-Process -FilePath 'powershell.exe' `
                -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$scriptPath`" -Root `"$Root`"" `
                -WindowStyle Minimized
            exit 0
        }

        # Oldest first, so a caller that queues several jobs gets them in order.
        $pending = Get-ChildItem -Path $jobsDir -Filter '*.cmd' -File | Sort-Object CreationTimeUtc
        foreach ($job in $pending) {
            try { Invoke-Job -JobFile $job.FullName }
            catch { Write-Output "job $($job.Name) failed: $_" }
        }
    }
    catch {
        Write-Output "[$(Get-Date -Format HH:mm:ss)] broker loop error: $_"
    }

    Start-Sleep -Milliseconds $PollMilliseconds
}
