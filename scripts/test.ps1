#requires -Version 7.0
<#
.SYNOPSIS
    Runs all tests in the AiNetCodeNavigator solution and writes the complete console output
    to a fixed file at temp/test.log and test results to TestResults/.

.DESCRIPTION
    Agents and automated workflows can inspect the complete execution output
    at the following path:
    <RepoRoot>/temp/test.log
#>
[CmdletBinding()]
param(
    [string]$Filter = '',
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
$resultsDir = Join-Path $repoRoot 'TestResults'

if (-not (Test-Path $tempDir)) {
    New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
}
if (-not (Test-Path $resultsDir)) {
    New-Item -ItemType Directory -Path $resultsDir -Force | Out-Null
}

$logFile = Join-Path $tempDir 'test.log'
$solutionPath = Join-Path $repoRoot 'AiNetCodeNavigator.slnx'

Write-Host "[INFO] Starting all tests for AiNetCodeNavigator.slnx..." -ForegroundColor Cyan
Write-Host "[NOTE] Agents can read the complete output at: $logFile" -ForegroundColor Yellow

$testArgs = @(
    'test',
    $solutionPath,
    # Keep repository snapshot checks isolated from other suites' generated files.
    '-m:1',
    '--results-directory', $resultsDir
)
if ($Filter) {
    $testArgs += @('--filter', $Filter)
}
if ($AdditionalArgs) {
    $testArgs += $AdditionalArgs
}

& dotnet @testArgs 2>&1 | Tee-Object -FilePath $logFile
$exitCode = $LASTEXITCODE

if ($exitCode -eq 0) {
    Write-Host "[INFO] All tests completed successfully. Log: $logFile" -ForegroundColor Green
} else {
    Write-Host "[ERROR] Tests failed with exit code $exitCode. Log: $logFile" -ForegroundColor Red
}

exit $exitCode
