# One-time setup, run as administrator ON the test machine. Installs SSH, keeps the console session
# awake and unlocked, and registers the broker to start with that session.
#
# What it deliberately does not do is listed at the end: those steps have no reliable API and must be
# done by hand once.
param(
    [string]$Root = 'C:\mewui-remote-test',
    [switch]$SkipSsh
)

$ErrorActionPreference = 'Stop'

$identity = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $identity.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this in an elevated PowerShell.'
}

$here = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Output '== creating the job root'
foreach ($dir in @($Root, (Join-Path $Root 'jobs'), (Join-Path $Root 'running'), (Join-Path $Root 'results'), (Join-Path $Root 'publish'))) {
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
}

Copy-Item (Join-Path $here 'broker.ps1') $Root -Force
Copy-Item (Join-Path $here 'wait-job.ps1') $Root -Force

if (-not $SkipSsh) {
    Write-Output '== installing OpenSSH Server'
    $server = Get-WindowsCapability -Online -Name 'OpenSSH.Server*'
    if ($server.State -ne 'Installed') { Add-WindowsCapability -Online -Name $server.Name | Out-Null }

    Set-Service -Name sshd -StartupType Automatic
    Start-Service sshd

    # PowerShell as the login shell keeps the client script's remote commands in one dialect.
    New-ItemProperty -Path 'HKLM:\SOFTWARE\OpenSSH' -Name DefaultShell `
        -Value "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" `
        -PropertyType String -Force | Out-Null

    # Matched by port, not by rule name: Windows names the rule its own OpenSSH feature installs with a
    # GUID, so a name check finds nothing and adds a second rule for a port that is already open.
    $sshRule = Get-NetFirewallRule -Enabled True -Direction Inbound -ErrorAction SilentlyContinue |
        Where-Object { ($_ | Get-NetFirewallPortFilter).LocalPort -eq 22 }
    if (-not $sshRule) {
        New-NetFirewallRule -Name sshd -DisplayName 'OpenSSH Server (sshd)' `
            -Enabled True -Direction Inbound -Protocol TCP -Action Allow -LocalPort 22 | Out-Null
    }
}

Write-Output '== keeping the console session awake'
# A sleeping machine has no session to run GUI tests in; a blanked monitor is harmless but the lock
# screen is not, so the screen saver's lock is cleared too.
powercfg /change standby-timeout-ac 0
powercfg /change monitor-timeout-ac 0
powercfg /change hibernate-timeout-ac 0
Set-ItemProperty -Path 'HKCU:\Control Panel\Desktop' -Name ScreenSaveActive -Value '0'
Set-ItemProperty -Path 'HKCU:\Control Panel\Desktop' -Name ScreenSaverIsSecure -Value '0'

Write-Output '== starting the broker with the console session'
# The Startup folder runs as the logged-on user in the interactive session, which is the only place the
# physical monitors exist. Anything launched over SSH lands in the sshd service session instead.
$brokerPath = Join-Path $Root 'broker.ps1'
$startup = [Environment]::GetFolderPath('Startup')
$linkPath = Join-Path $startup 'MewUI Remote Test Broker.lnk'

$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($linkPath)
$link.TargetPath = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
$link.Arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$brokerPath`" -Root `"$Root`""
$link.WorkingDirectory = $Root
$link.WindowStyle = 7
$link.Description = 'Runs MewUI GUI tests in the physical console session'
$link.Save()

Write-Output "   $linkPath"

# An earlier revision of this script started the broker from a scheduled task; leaving both registered
# would run two brokers against one jobs folder.
if (Get-ScheduledTask -TaskName 'MewUI-RemoteTestBroker' -ErrorAction SilentlyContinue) {
    Unregister-ScheduledTask -TaskName 'MewUI-RemoteTestBroker' -Confirm:$false
    Write-Output '   removed the previous scheduled task'
}

# Starting it from here only works when "here" is the console session. Over SSH this process lives in
# the sshd service session, where a broker would see one placeholder display and no physical monitors,
# and it would still hold the jobs folder.
if ($env:SESSIONNAME -eq 'Console') {
    Start-Process -FilePath $link.TargetPath -ArgumentList $link.Arguments -WindowStyle Minimized
    Write-Output '   broker started'
}
else {
    Write-Output "   not the console session (SESSIONNAME='$env:SESSIONNAME'), so the broker was NOT started"
    Write-Output '   start it at the physical console: run the Startup shortcut, or sign out and back in'
}

Write-Output ''
Write-Output "Done. The broker starts with $env:USERNAME's session; if you elevated as a different"
Write-Output 'account, re-run this while logged in as the account that will hold the console session.'
Write-Output ''
Write-Output 'Four steps have no reliable API and are left to you:'
Write-Output ''
Write-Output '  1. Public key: append the dev machine key to C:\ProgramData\ssh\administrators_authorized_keys'
Write-Output '     (an admin account ignores %USERPROFILE%\.ssh\authorized_keys), then'
Write-Output '     icacls that file /inheritance:r /grant "SYSTEM:F" /grant "Administrators:F"'
Write-Output '  2. Per-monitor scale: Settings > System > Display, one monitor at a time.'
Write-Output '  3. Auto sign-in, so the console session returns after a reboot: netplwiz, clear'
Write-Output '     "Users must enter a user name and password".'
Write-Output '  4. Never use RDP against this machine: an RDP session replaces the physical monitors'
Write-Output '     with one virtual display and the scale matrix disappears.'
