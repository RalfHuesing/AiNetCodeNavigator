#requires -Version 7.0
<#
.SYNOPSIS
    Builds the solution, executes automated tests, and deploys the MCP server and
    assembly export executables to a testable output directory.

.DESCRIPTION
    Executes a complete local release/deployment pipeline for both executables:
    1. Builds the solution (AiNetCodeNavigator.slnx).
    2. Runs tests (routine solution suite via scripts/test.ps1, or scripts/test-fast.ps1 if -FastTestsOnly).
    3. Publishes the MCP server and assembly export CLI executables and their
       shared dependencies directly to the specified directory root.
    
    The default deploy target directory is <RepoRoot>/deploy, which is excluded by .gitignore.
    Console output is logged to temp/deploy.log.

.PARAMETER OutputDir
    Destination path for both deployed executables. Defaults to <RepoRoot>/deploy.

.PARAMETER Configuration
    Build and publish configuration. Defaults to 'Release'.

.PARAMETER FastTestsOnly
    Runs only FastTests instead of the complete routine test suite.

.PARAMETER SkipTests
    Skips the automated test run (for rapid iteration).

.PARAMETER IncludeExtended
    Includes ExtendedIntegration tests in the test suite run.

.PARAMETER Clean
    Cleans the target output directory before publishing.
#>
[CmdletBinding()]
param(
    [string]$OutputDir = '',
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    [switch]$FastTestsOnly,
    [switch]$SkipTests,
    [switch]$IncludeExtended,
    [switch]$Clean,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$AdditionalPublishArgs
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

$logFile = Join-Path $tempDir 'deploy.log'

# Resolve destination directory (defaults to <RepoRoot>/deploy)
$resolvedOutputDir = if ($OutputDir) {
    if ([System.IO.Path]::IsPathRooted($OutputDir)) {
        $OutputDir
    } else {
        [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $OutputDir))
    }
} else {
    Join-Path $repoRoot 'deploy'
}

$timestamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
"=== AiNetCodeNavigator Deployment Started: $timestamp ===" | Out-File -FilePath $logFile -Encoding utf8
"Configuration: $Configuration" | Out-File -FilePath $logFile -Append -Encoding utf8
"Destination:   $resolvedOutputDir" | Out-File -FilePath $logFile -Append -Encoding utf8

Write-Host "[INFO] Starting deployment pipeline for AiNetCodeNavigator..." -ForegroundColor Cyan
Write-Host "[NOTE] Deployment target: $resolvedOutputDir" -ForegroundColor Yellow
Write-Host "[NOTE] Full log file:     $logFile" -ForegroundColor DarkGray

# -----------------------------------------------------------------------------
# STEP 1: Build Solution
# -----------------------------------------------------------------------------
Write-Host "`n[STEP 1/3] Building solution ($Configuration)..." -ForegroundColor Cyan
"[INFO] Building solution ($Configuration)..." | Out-File -FilePath $logFile -Append -Encoding utf8

$solutionPath = Join-Path $repoRoot 'AiNetCodeNavigator.slnx'
& dotnet build $solutionPath -c $Configuration 2>&1 | Tee-Object -FilePath $logFile -Append
$buildExitCode = $LASTEXITCODE

if ($buildExitCode -ne 0) {
    Write-Host "[ERROR] Build failed with exit code $buildExitCode. Log: $logFile" -ForegroundColor Red
    exit $buildExitCode
}
Write-Host "[INFO] Build succeeded." -ForegroundColor Green

# -----------------------------------------------------------------------------
# STEP 2: Run Tests
# -----------------------------------------------------------------------------
if ($SkipTests) {
    Write-Host "`n[STEP 2/3] Skipping tests (-SkipTests specified)." -ForegroundColor Yellow
    "[INFO] Skipping tests (-SkipTests specified)." | Out-File -FilePath $logFile -Append -Encoding utf8
} else {
    Write-Host "`n[STEP 2/3] Running tests..." -ForegroundColor Cyan
    $testScript = if ($FastTestsOnly) {
        Join-Path $repoRoot 'scripts/test-fast.ps1'
    } else {
        Join-Path $repoRoot 'scripts/test.ps1'
    }

    $testArgs = @('-File', $testScript)
    if ($IncludeExtended -and -not $FastTestsOnly) {
        $testArgs += '-IncludeExtended'
    }

    & pwsh @testArgs 2>&1 | Tee-Object -FilePath $logFile -Append
    $testExitCode = $LASTEXITCODE

    if ($testExitCode -ne 0) {
        Write-Host "[ERROR] Tests failed with exit code $testExitCode. Log: $logFile" -ForegroundColor Red
        exit $testExitCode
    }
    Write-Host "[INFO] Tests succeeded." -ForegroundColor Green
}

# -----------------------------------------------------------------------------
# STEP 3: Publish both executables
# -----------------------------------------------------------------------------
Write-Host "`n[STEP 3/3] Deploying executables to $resolvedOutputDir..." -ForegroundColor Cyan
"[INFO] Publishing MCP server and assembly export projects to $resolvedOutputDir..." | Out-File -FilePath $logFile -Append -Encoding utf8

if ($Clean -and (Test-Path $resolvedOutputDir)) {
    Write-Host "[INFO] Cleaning existing output directory (-Clean specified)..." -ForegroundColor Yellow
    Get-ChildItem -Path $resolvedOutputDir -Recurse | Remove-Item -Force -Recurse
}

if (-not (Test-Path $resolvedOutputDir)) {
    New-Item -ItemType Directory -Path $resolvedOutputDir -Force | Out-Null
}

function Publish-ExecutableProject {
    param(
        [string]$ProjectPath,
        [string]$Destination,
        [string]$ExpectedExecutable
    )

    $publishArgs = @('publish', $ProjectPath, '-c', $Configuration, '-o', $Destination)
    if ($AdditionalPublishArgs) {
        $publishArgs += $AdditionalPublishArgs
    }

    & dotnet @publishArgs 2>&1 | Tee-Object -FilePath $logFile -Append | Out-Host
    $publishExitCode = $LASTEXITCODE
    if ($publishExitCode -ne 0) {
        Write-Host "[ERROR] Publish failed with exit code $publishExitCode for $ProjectPath. Log: $logFile" -ForegroundColor Red
        exit $publishExitCode
    }

    $executablePath = Join-Path $Destination $ExpectedExecutable
    if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
        Write-Host "[ERROR] Expected executable not found at: $executablePath" -ForegroundColor Red
        exit 1
    }
    return $executablePath
}

$mcpProjectPath = Join-Path $repoRoot 'src/AiNetCodeNavigator/AiNetCodeNavigator.csproj'
$exportProjectPath = Join-Path $repoRoot 'src/AiNetCodeNavigator.AssemblyExport/AiNetCodeNavigator.AssemblyExport.csproj'
$exePath = Publish-ExecutableProject -ProjectPath $mcpProjectPath -Destination $resolvedOutputDir -ExpectedExecutable 'AiNetCodeNavigator.exe'
$exportExePath = Publish-ExecutableProject -ProjectPath $exportProjectPath -Destination $resolvedOutputDir -ExpectedExecutable 'AiNetCodeNavigator.AssemblyExport.exe'

# Ensure hostsettings.json exists in target directory
$settingsPath = Join-Path $resolvedOutputDir 'hostsettings.json'
if (-not (Test-Path $settingsPath)) {
    Copy-Item -LiteralPath (Join-Path $repoRoot 'hostsettings.json') -Destination $settingsPath
}

$escapedExePath = $exePath.Replace('\', '\\')
$escapedSettingsPath = $settingsPath.Replace('\', '\\')

Write-Host "`n========================================================" -ForegroundColor Green
Write-Host "[SUCCESS] AiNetCodeNavigator executables deployed successfully!" -ForegroundColor Green
Write-Host "========================================================" -ForegroundColor Green
Write-Host "Executable: $exePath" -ForegroundColor Cyan
Write-Host "Exporter:   $exportExePath" -ForegroundColor Cyan
Write-Host "Settings:   $settingsPath" -ForegroundColor Cyan
Write-Host "Log file:   $logFile" -ForegroundColor DarkGray
Write-Host "`nYou can now test the server directly with your MCP client:" -ForegroundColor Yellow
Write-Host @"

Configuration for Cursor (.cursor/mcp.json or ~/.cursor/mcp.json):
{
  "mcpServers": {
    "AiNetCodeNavigator": {
      "type": "stdio",
      "command": "$escapedExePath",
      "args": ["--config", "$escapedSettingsPath"]
    }
  }
}

Configuration for Claude Desktop (%APPDATA%\Claude\claude_desktop_config.json):
{
  "mcpServers": {
    "AiNetCodeNavigator": {
      "command": "$escapedExePath",
      "args": ["--config", "$escapedSettingsPath"]
    }
  }
}

Configuration for Antigravity IDE (.agents/mcp_config.json or ~/.gemini/config/mcp_config.json):
{
  "mcpServers": {
    "AiNetCodeNavigator": {
      "command": "$escapedExePath",
      "args": ["--config", "$escapedSettingsPath"]
    }
  }
}
"@ -ForegroundColor Gray

Write-Host "`nRun the offline exporter separately with:`n  `"$exportExePath`" <output-directory> <source-dll-or-pattern> [<source-dll-or-pattern> ...]" -ForegroundColor Yellow

exit 0
