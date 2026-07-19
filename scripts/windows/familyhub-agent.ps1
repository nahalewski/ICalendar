<#
.SYNOPSIS
    Startup agent: updates once, launches the dashboard, then keeps both running.

.DESCRIPTION
    Runs for the life of the session. It updates on boot so the display comes up on the latest
    commit, then polls GitHub on an interval, rebuilding and restarting only when a build succeeds.
    It also restarts the app if it exits for any other reason, so a crash does not leave a blank
    wall display until someone notices.
#>
[CmdletBinding()]
param(
    [string]$RepoPath,
    [string]$Branch          = 'main',
    [string]$RuntimeDir      = (Join-Path $env:LOCALAPPDATA 'FamilyHub\runtime'),
    [int]   $CheckEveryMinutes = 5
)

$ErrorActionPreference = 'Continue'

# $PSScriptRoot is not reliably populated in param defaults, so resolve paths here instead.
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($RepoPath)) {
    $RepoPath = Split-Path -Parent (Split-Path -Parent $scriptDir)
}
$current = Join-Path $RuntimeDir 'current'
$exePath = Join-Path $current 'FamilyHub.App.exe'
$updater = Join-Path $scriptDir 'familyhub-update.ps1'
$logDir  = Join-Path $env:LOCALAPPDATA 'FamilyHub\logs'
$logFile = Join-Path $logDir 'agent.log'

if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Force -Path $logDir | Out-Null }

function Write-Log {
    param([string]$Message)
    $line = "{0} [AGENT] {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Message
    Write-Host $line
    Add-Content -Path $logFile -Value $line -Encoding utf8
}

if ((Test-Path $logFile) -and ((Get-Item $logFile).Length -gt 1MB)) {
    Set-Content -Path $logFile -Value (Get-Content $logFile -Tail 500) -Encoding utf8
}

function Invoke-Update {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $updater `
        -RepoPath $RepoPath -Branch $Branch -RuntimeDir $RuntimeDir | Out-Null
    return $LASTEXITCODE
}

function Get-App { Get-Process -Name 'FamilyHub.App' -ErrorAction SilentlyContinue }

function Start-App {
    if (-not (Test-Path $exePath)) { Write-Log "No build at $exePath"; return }
    if (Get-App) { return }
    Write-Log 'Starting dashboard'
    Start-Process -FilePath $exePath -WorkingDirectory $current | Out-Null
}

Write-Log "Agent starting. Repo=$RepoPath Branch=$Branch Interval=${CheckEveryMinutes}m"

# Update before first launch so a reboot always lands on the newest commit.
$code = Invoke-Update
if ($code -eq 1) { Write-Log 'Startup update failed; falling back to the existing build.' }
Start-App

$nextCheck = (Get-Date).AddMinutes($CheckEveryMinutes)
while ($true) {
    Start-Sleep -Seconds 15

    # Restart the dashboard if it died for any reason.
    if (-not (Get-App)) { Write-Log 'Dashboard not running; restarting'; Start-App }

    if ((Get-Date) -ge $nextCheck) {
        $nextCheck = (Get-Date).AddMinutes($CheckEveryMinutes)
        $code = Invoke-Update
        if ($code -eq 10) {
            Write-Log 'Update applied; relaunching dashboard'
            Start-App
        } elseif ($code -eq 1) {
            Write-Log 'Update failed; previous build still running. See update.log'
        }
    }
}
