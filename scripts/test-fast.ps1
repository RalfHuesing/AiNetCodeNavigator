#requires -Version 7.0
<#
.SYNOPSIS
    Runs FastTests for AiNetCodeNavigator and writes the complete console output
    to a fixed file at temp/test-fast.log and test results to TestResults/FastTests.trx.

.DESCRIPTION
    Agents and automated workflows can inspect the complete execution output
    at the following path. E2EIntegration tests are always excluded by the script.
    <RepoRoot>/temp/test-fast.log
#>
[CmdletBinding()]
param(
    [string]$Filter = '',
    [switch]$ReviewReportsOnly,
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

$logFile = Join-Path $tempDir 'test-fast.log'
$trxFile = 'FastTests.trx'
$projectPath = Join-Path $repoRoot 'tests/AiNetCodeNavigator.FastTests/AiNetCodeNavigator.FastTests.csproj'

Write-Host "[INFO] Starting FastTests..." -ForegroundColor Cyan
Write-Host "[NOTE] Agents can read the complete output at: $logFile" -ForegroundColor Yellow
Write-Host "[NOTE] Fixed test results file (TRX): $(Join-Path $resultsDir $trxFile)" -ForegroundColor DarkGray

$testArgs = @(
    'test',
    $projectPath,
    '--logger', "trx;LogFileName=$trxFile",
    '--results-directory', $resultsDir
)
$reviewReportsFilter = 'FullyQualifiedName=AiNetCodeNavigator.FastTests.Reporting.RepositoryAuditReportTests.Review_PublishesRepositoryReportsWithoutBaseline'
if ($ReviewReportsOnly) {
    if ($Filter) {
        throw '-ReviewReportsOnly selects one fixed report test; do not combine it with -Filter.'
    }
    $effectiveFilter = $reviewReportsFilter
    Write-Host '[NOTE] Only the explicitly opted-in repository report test is selected; this does not enable other E2EIntegration tests.' -ForegroundColor Yellow
} else {
    $effectiveFilter = 'Category!=E2EIntegration'
    if ($Filter) {
        $effectiveFilter = "($Filter)&($effectiveFilter)"
    }
    Write-Host '[NOTE] E2EIntegration tests are excluded.' -ForegroundColor Yellow
}
$testArgs += @('--filter', $effectiveFilter)
if ($AdditionalArgs) {
    if ($AdditionalArgs | Where-Object { $_ -match '^(--filter|-filter)(=|$)' }) {
        throw 'Pass test selection through -Filter; additional --filter arguments cannot override the E2E exclusion.'
    }
    $testArgs += $AdditionalArgs
}

& dotnet @testArgs 2>&1 | Tee-Object -FilePath $logFile
$exitCode = $LASTEXITCODE

if ($exitCode -eq 0) {
    Write-Host "[INFO] FastTests completed successfully. Log: $logFile" -ForegroundColor Green
} else {
    Write-Host "[ERROR] FastTests failed with exit code $exitCode. Log: $logFile" -ForegroundColor Red
}

exit $exitCode
