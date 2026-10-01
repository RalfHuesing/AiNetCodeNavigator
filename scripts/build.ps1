#requires -Version 7.0
<#
.SYNOPSIS
    Builds the AiNetCodeNavigator solution and writes the complete console output
    to a fixed log file at temp/build.log.

.DESCRIPTION
    Agents and automated workflows can inspect the complete execution output
    at the following path:
    <RepoRoot>/temp/build.log
#>
[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$AdditionalArgs
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-RepoRoot {
    $current = $PSScriptRoot
    while ($current) {
        if (Test-Path (Join-Path $current 'AiNetCodeNavigator.slnx')) {
            return (Resolve-Path $current).Path
        }
        $parent = Split-Path $current -Parent
        if ($parent -eq $current) { break }
        $current = $parent
    }
    throw "Could not locate the repository root containing 'AiNetCodeNavigator.slnx'."
}

$repoRoot = Get-RepoRoot
$tempDir = Join-Path $repoRoot 'temp'
if (-not (Test-Path $tempDir)) {
    New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
}

$logFile = Join-Path $tempDir 'build.log'
$solutionPath = Join-Path $repoRoot 'AiNetCodeNavigator.slnx'

Write-Host "[INFO] Starting build for AiNetCodeNavigator.slnx..." -ForegroundColor Cyan
Write-Host "[NOTE] Agents can read the complete output at: $logFile" -ForegroundColor Yellow

$buildArgs = @('build', $solutionPath)
if ($AdditionalArgs) {
    $buildArgs += $AdditionalArgs
}

& dotnet @buildArgs 2>&1 | Tee-Object -FilePath $logFile
$exitCode = $LASTEXITCODE

if ($exitCode -eq 0) {
    Write-Host "[INFO] Build completed successfully. Log: $logFile" -ForegroundColor Green
} else {
    Write-Host "[ERROR] Build failed with exit code $exitCode. Log: $logFile" -ForegroundColor Red
}

exit $exitCode
