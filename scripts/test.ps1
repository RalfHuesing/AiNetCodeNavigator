#requires -Version 7.0
<#
.SYNOPSIS
    Runs routine tests in the AiNetCodeNavigator solution and writes the complete console output
    to a fixed file at temp/test.log and test results to TestResults/.

.DESCRIPTION
    ExtendedIntegration and E2EIntegration tests are excluded by default. Use
    -IncludeExtended to opt into ExtendedIntegration; E2EIntegration remains excluded.

    Agents and automated workflows can inspect the complete execution output
    at the following path:
    <RepoRoot>/temp/test.log
#>
[CmdletBinding()]
param(
    [string]$Filter = '',
    [switch]$IncludeExtended,
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

Write-Host "[INFO] Starting tests for AiNetCodeNavigator.slnx..." -ForegroundColor Cyan
Write-Host "[NOTE] Agents can read the complete output at: $logFile" -ForegroundColor Yellow

$testArgs = @(
    'test',
    $solutionPath,
    # Keep repository snapshot checks isolated from other suites' generated files.
    '-m:1',
    '--results-directory', $resultsDir
)
$exclusions = @('Category!=E2EIntegration')
if (-not $IncludeExtended) {
    $exclusions += 'Category!=ExtendedIntegration'
    Write-Host '[NOTE] ExtendedIntegration tests are excluded. Use -IncludeExtended to opt in; E2EIntegration tests remain excluded.' -ForegroundColor Yellow
} else {
    Write-Host '[NOTE] E2EIntegration tests are always excluded.' -ForegroundColor Yellow
}
$effectiveFilter = ($exclusions -join '&')
if ($Filter) {
    $effectiveFilter = "($Filter)&($effectiveFilter)"
}
if ($effectiveFilter) {
    $testArgs += @('--filter', $effectiveFilter)
}
if ($AdditionalArgs) {
    if ($AdditionalArgs | Where-Object { $_ -match '^(--filter|-filter)(=|$)' }) {
        throw 'Pass test selection through -Filter; additional --filter arguments cannot override the E2E exclusion.'
    }
    $testArgs += $AdditionalArgs
}

& dotnet @testArgs 2>&1 | Tee-Object -FilePath $logFile
$exitCode = $LASTEXITCODE

if ($exitCode -eq 0) {
    Write-Host "[INFO] Selected tests completed successfully. Log: $logFile" -ForegroundColor Green
} else {
    Write-Host "[ERROR] Tests failed with exit code $exitCode. Log: $logFile" -ForegroundColor Red
}

exit $exitCode
