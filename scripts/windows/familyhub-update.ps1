<#
.SYNOPSIS
    Checks GitHub for changes, and rebuilds FamilyHub only if the build succeeds.

.DESCRIPTION
    One update cycle: fetch, compare, pull, publish to a staging directory, and swap staging into
    place only after a clean build. A failed build leaves the running dashboard untouched — the wall
    display must never be left dead because someone pushed a broken commit.

    Exit codes: 0 up to date, 10 updated (caller should restart the app), 1 failed.
#>
[CmdletBinding()]
param(
    [string]$RepoPath,
    [string]$Branch     = 'main',
    [string]$RuntimeDir = (Join-Path $env:LOCALAPPDATA 'FamilyHub\runtime'),
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

# $PSScriptRoot is not reliably populated in param defaults, so resolve the repo here instead.
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($RepoPath)) {
    $RepoPath = Split-Path -Parent (Split-Path -Parent $scriptDir)
}
$project  = Join-Path $RepoPath 'src\FamilyHub.App\FamilyHub.App.csproj'
$current  = Join-Path $RuntimeDir 'current'
$staging  = Join-Path $RuntimeDir 'staging'
$logDir   = Join-Path $env:LOCALAPPDATA 'FamilyHub\logs'
$logFile  = Join-Path $logDir 'update.log'
$lockFile = Join-Path $RuntimeDir '.update.lock'

foreach ($dir in @($RuntimeDir, $logDir)) {
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
}

function Write-Log {
    param([string]$Message, [string]$Level = 'INFO')
    $line = "{0} [{1}] {2}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Level, $Message
    Write-Host $line
    Add-Content -Path $logFile -Value $line -Encoding utf8
}

# Keep the log from growing without bound on a device that runs for months.
if ((Test-Path $logFile) -and ((Get-Item $logFile).Length -gt 1MB)) {
    $keep = Get-Content $logFile -Tail 500
    Set-Content -Path $logFile -Value $keep -Encoding utf8
}

# A single lock stops the startup agent and a manual run from building over each other.
$lock = $null
try {
    $lock = [System.IO.File]::Open($lockFile, 'OpenOrCreate', 'ReadWrite', 'None')
} catch {
    Write-Log 'Another update is already running; skipping this cycle.' 'WARN'
    exit 0
}

try {
    if (-not (Test-Path $project)) { Write-Log "Project not found at $project" 'ERROR'; exit 1 }

    Push-Location $RepoPath
    try {
        # --- Is there anything new? ------------------------------------------------------------
        Write-Log "Checking origin/$Branch for changes"
        git fetch --quiet origin $Branch 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { Write-Log 'Fetch failed (offline?); leaving the running build alone.' 'WARN'; exit 0 }

        $localSha  = (git rev-parse HEAD).Trim()
        $remoteSha = (git rev-parse "origin/$Branch").Trim()

        $haveBuild = Test-Path (Join-Path $current 'FamilyHub.App.exe')
        if ($localSha -eq $remoteSha -and $haveBuild -and -not $Force) {
            Write-Log "Up to date at $($localSha.Substring(0,7))"
            exit 0
        }

        # --- Pull ------------------------------------------------------------------------------
        if ($localSha -ne $remoteSha) {
            Write-Log "Update available: $($localSha.Substring(0,7)) -> $($remoteSha.Substring(0,7))"
            git pull --ff-only origin $Branch 2>&1 | ForEach-Object { Write-Log $_ }
            if ($LASTEXITCODE -ne 0) {
                # Refuse to clobber local commits or edits; a human should look at it.
                Write-Log 'Fast-forward pull failed. Local changes or diverged history — resolve manually.' 'ERROR'
                exit 1
            }
        } else {
            Write-Log 'No new commits, but no usable build present; rebuilding.'
        }

        # --- Build into staging, never over the running copy ------------------------------------
        if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
        Write-Log 'Publishing (Release)'
        $publishLog = & dotnet publish $project -c Release -o $staging --nologo 2>&1
        if ($LASTEXITCODE -ne 0) {
            $publishLog | Select-Object -Last 25 | ForEach-Object { Write-Log $_ 'ERROR' }
            Write-Log 'Build FAILED. Keeping the previous build running.' 'ERROR'
            exit 1
        }

        # --- Swap in ---------------------------------------------------------------------------
        Write-Log 'Build succeeded; swapping into place'
        Get-Process -Name 'FamilyHub.App' -ErrorAction SilentlyContinue | ForEach-Object {
            Write-Log "Stopping running app (PID $($_.Id))"
            try { $_.Kill(); $_.WaitForExit(10000) | Out-Null } catch {}
        }
        Start-Sleep -Milliseconds 500

        $previous = Join-Path $RuntimeDir 'previous'
        if (Test-Path $previous) { Remove-Item $previous -Recurse -Force }
        if (Test-Path $current)  { Move-Item $current $previous }
        Move-Item $staging $current

        Write-Log "Updated to $($remoteSha.Substring(0,7))"
        exit 10
    }
    finally { Pop-Location }
}
catch {
    Write-Log "Unexpected failure: $($_.Exception.Message)" 'ERROR'
    exit 1
}
finally {
    if ($lock) { $lock.Close(); Remove-Item $lockFile -Force -ErrorAction SilentlyContinue }
}
