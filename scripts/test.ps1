#requires -Version 7.0
<#
.SYNOPSIS
    Führt alle Tests der AiNetCodeNavigator-Solution aus und schreibt den vollständigen Konsolen-Output
    in eine statische Datei unter temp/test.log sowie Testergebnisse nach TestResults/.

.DESCRIPTION
    Agenten und automatisierte Workflows können den vollständigen Output der Ausführung
    unter folgendem Pfad einsehen:
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

$logFile = Join-Path $tempDir 'test.log'
$solutionPath = Join-Path $repoRoot 'AiNetCodeNavigator.slnx'

Write-Host "[INFO] Starte alle Tests für AiNetCodeNavigator.slnx..." -ForegroundColor Cyan
Write-Host "[HINWEIS] Agenten können den vollständigen Output unter folgendem Pfad lesen: $logFile" -ForegroundColor Yellow

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
    Write-Host "[INFO] Alle Tests erfolgreich abgeschlossen. Log: $logFile" -ForegroundColor Green
} else {
    Write-Host "[ERROR] Tests fehlgeschlagen mit Exit-Code $exitCode. Log: $logFile" -ForegroundColor Red
}

exit $exitCode
