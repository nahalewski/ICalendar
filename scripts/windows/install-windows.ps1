<#
.SYNOPSIS
    Registers the FamilyHub agent to start at logon.

.DESCRIPTION
    Creates a Scheduled Task that launches the agent hidden at logon, which then keeps the
    dashboard updated and running. Run once. Use -Uninstall to remove it.

    A logon task (not a boot task) is deliberate: the dashboard needs an interactive desktop
    session to display on, which a SYSTEM service at boot does not have.
#>
[CmdletBinding()]
param(
    [string]$TaskName          = 'FamilyHub Dashboard',
    [string]$Branch            = 'main',
    [int]   $CheckEveryMinutes = 5,
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'

# $PSScriptRoot is not reliably populated in param defaults, so resolve paths here instead.
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoPath  = Split-Path -Parent (Split-Path -Parent $scriptDir)
$agentPath = Join-Path $scriptDir 'familyhub-agent.ps1'

if ($Uninstall) {
    if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
        Write-Host "Removed scheduled task '$TaskName'."
    } else {
        Write-Host "No scheduled task named '$TaskName'."
    }
    Write-Host 'Note: the runtime build and logs under %LOCALAPPDATA%\FamilyHub were left in place.'
    return
}

if (-not (Test-Path $agentPath)) { throw "Agent script not found at $agentPath" }

# Verify the toolchain now rather than failing silently at 6am on a wall display.
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) { throw 'dotnet SDK not found on PATH. Install the .NET 10 SDK first.' }
$git = Get-Command git -ErrorAction SilentlyContinue
if (-not $git) { throw 'git not found on PATH. Install Git first.' }

$arguments = '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "{0}" -RepoPath "{1}" -Branch {2} -CheckEveryMinutes {3}' `
    -f $agentPath, $repoPath, $Branch, $CheckEveryMinutes

$action    = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $arguments
$trigger   = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
$principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Limited
$settings  = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -StartWhenAvailable -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)

if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger `
    -Principal $principal -Settings $settings -Description 'Keeps the FamilyHub dashboard updated from GitHub and running.' | Out-Null

Write-Host "Installed '$TaskName'."
Write-Host "  Repo:     $repoPath"
Write-Host "  Branch:   $Branch"
Write-Host "  Interval: every $CheckEveryMinutes minute(s)"
Write-Host "  Logs:     $(Join-Path $env:LOCALAPPDATA 'FamilyHub\logs')"
Write-Host ''
Write-Host 'Start it now without rebooting:'
Write-Host "  Start-ScheduledTask -TaskName '$TaskName'"
