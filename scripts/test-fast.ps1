#requires -Version 7.0
<#
.SYNOPSIS
    Führt FastTests für AiNetCodeNavigator aus und schreibt den vollständigen Konsolen-Output
    in eine statische Datei unter temp/test-fast.log sowie Testergebnisse nach TestResults/FastTests.trx.

.DESCRIPTION
    Agenten und automatisierte Workflows können den vollständigen Output der Ausführung
    unter folgendem Pfad einsehen:
    <RepoRoot>/temp/test-fast.log
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
    throw "Repository-Root mit 'AiNetCodeNavigator.slnx' konnte nicht ermittelt werden."
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

Write-Host "[INFO] Starte FastTests..." -ForegroundColor Cyan
Write-Host "[HINWEIS] Agenten können den vollständigen Output unter folgendem Pfad lesen: $logFile" -ForegroundColor Yellow
Write-Host "[HINWEIS] Statische Testergebnisdatei (TRX): $(Join-Path $resultsDir $trxFile)" -ForegroundColor DarkGray

$testArgs = @(
    'test',
    $projectPath,
    '--logger', "trx;LogFileName=$trxFile",
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
    Write-Host "[INFO] FastTests erfolgreich abgeschlossen. Log: $logFile" -ForegroundColor Green
} else {
    Write-Host "[ERROR] FastTests fehlgeschlagen mit Exit-Code $exitCode. Log: $logFile" -ForegroundColor Red
}

exit $exitCode
