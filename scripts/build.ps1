#requires -Version 7.0
<#
.SYNOPSIS
    Baut die AiNetCodeNavigator-Solution und schreibt den vollständigen Konsolen-Output
    in eine statische Logdatei unter temp/build.log.

.DESCRIPTION
    Agenten und automatisierte Workflows können den vollständigen Output der Ausführung
    unter folgendem Pfad einsehen:
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
    throw "Repository-Root mit 'AiNetCodeNavigator.slnx' konnte nicht ermittelt werden."
}

$repoRoot = Get-RepoRoot
$tempDir = Join-Path $repoRoot 'temp'
if (-not (Test-Path $tempDir)) {
    New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
}

$logFile = Join-Path $tempDir 'build.log'
$solutionPath = Join-Path $repoRoot 'AiNetCodeNavigator.slnx'

Write-Host "[INFO] Starte Build für AiNetCodeNavigator.slnx..." -ForegroundColor Cyan
Write-Host "[HINWEIS] Agenten können den vollständigen Output unter folgendem Pfad lesen: $logFile" -ForegroundColor Yellow

$buildArgs = @('build', $solutionPath)
if ($AdditionalArgs) {
    $buildArgs += $AdditionalArgs
}

& dotnet @buildArgs 2>&1 | Tee-Object -FilePath $logFile
$exitCode = $LASTEXITCODE

if ($exitCode -eq 0) {
    Write-Host "[INFO] Build erfolgreich abgeschlossen. Log: $logFile" -ForegroundColor Green
} else {
    Write-Host "[ERROR] Build fehlgeschlagen mit Exit-Code $exitCode. Log: $logFile" -ForegroundColor Red
}

exit $exitCode
