#requires -Version 7.0
<#
.SYNOPSIS
    Runs IntegrationTests for AiNetCodeNavigator and writes the complete console output
    to a fixed file at temp/test-integration.log and test results to TestResults/IntegrationTests.trx.

.DESCRIPTION
    ExtendedIntegration tests are excluded by default. Use -IncludeExtended to opt in;
    combine it with -Filter for a focused extended run.

    Agents and automated workflows can inspect the complete execution output
    at the following path:
    <RepoRoot>/temp/test-integration.log
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

$logFile = Join-Path $tempDir 'test-integration.log'
$trxFile = 'IntegrationTests.trx'
$projectPath = Join-Path $repoRoot 'tests/AiNetCodeNavigator.IntegrationTests/AiNetCodeNavigator.IntegrationTests.csproj'

Write-Host "[INFO] Starting IntegrationTests..." -ForegroundColor Cyan
Write-Host "[NOTE] Agents can read the complete output at: $logFile" -ForegroundColor Yellow
Write-Host "[NOTE] Fixed test results file (TRX): $(Join-Path $resultsDir $trxFile)" -ForegroundColor DarkGray

$testArgs = @(
    'test',
    $projectPath,
    '--logger', "trx;LogFileName=$trxFile",
    '--results-directory', $resultsDir
)
$effectiveFilter = $Filter
if (-not $IncludeExtended) {
    $effectiveFilter = if ($Filter) { "($Filter)&(Category!=ExtendedIntegration)" } else { 'Category!=ExtendedIntegration' }
    Write-Host '[NOTE] ExtendedIntegration tests are excluded. Use -IncludeExtended to opt in.' -ForegroundColor Yellow
}
if ($effectiveFilter) {
    $testArgs += @('--filter', $effectiveFilter)
}
if ($AdditionalArgs) {
    $testArgs += $AdditionalArgs
}

& dotnet @testArgs 2>&1 | Tee-Object -FilePath $logFile
$exitCode = $LASTEXITCODE

if ($exitCode -eq 0) {
    Write-Host "[INFO] IntegrationTests completed successfully. Log: $logFile" -ForegroundColor Green
} else {
    Write-Host "[ERROR] IntegrationTests failed with exit code $exitCode. Log: $logFile" -ForegroundColor Red
}

exit $exitCode
