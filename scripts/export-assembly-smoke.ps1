#requires -Version 7.0
<#
.SYNOPSIS
    Exports the built Serilog.dll to temp/asm-dump and checks agent navigation artifacts.

.DESCRIPTION
    Builds through scripts/build.ps1 unless -SkipBuild is supplied. Serilog is a
    representative managed DLL with multiple namespaces and no selected third-party
    dependencies. The exporter replaces its marked temp/asm-dump directory on every
    run; an existing unmarked directory is rejected by the exporter. Inspect the
    generated README.md, assemblies.json and C# files for agent navigation quality.

.EXAMPLE
    pwsh -File ./scripts/export-assembly-smoke.ps1

.EXAMPLE
    pwsh -File ./scripts/export-assembly-smoke.ps1 -SkipBuild -Configuration Release
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent

if (-not $SkipBuild) {
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'build.ps1') -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE. See temp/build.log." }
}

$buildProperties = [xml](Get-Content -LiteralPath (Join-Path $repoRoot 'Directory.Build.props') -Raw)
$targetFramework = $buildProperties.SelectSingleNode('/Project/PropertyGroup/TargetFramework').InnerText
$binaryDirectory = Join-Path $repoRoot "src/AiNetCodeNavigator.AssemblyExport/bin/$Configuration/$targetFramework"
$exporter = Join-Path $binaryDirectory 'AiNetCodeNavigator.AssemblyExport.exe'
$assembly = Join-Path $binaryDirectory 'Serilog.dll'
foreach ($inputFile in @($exporter, $assembly)) {
    if (-not (Test-Path -LiteralPath $inputFile -PathType Leaf)) {
        throw "Missing built input: $inputFile. Run this script without -SkipBuild."
    }
}

$dumpDirectory = Join-Path $repoRoot 'temp/asm-dump'
Write-Host "[INFO] Exporting Serilog.dll to $dumpDirectory"
& $exporter $dumpDirectory $assembly
if ($LASTEXITCODE -ne 0) { throw "Assembly export failed with exit code $LASTEXITCODE. See temp/asm-dump/last-run.log." }

function Assert-DumpFile([string]$BaseDirectory, [string]$RelativePath) {
    if ([string]::IsNullOrWhiteSpace($RelativePath) -or [IO.Path]::IsPathRooted($RelativePath)) {
        throw "Expected a nonempty relative artifact path: '$RelativePath'."
    }
    $fullPath = [IO.Path]::GetFullPath((Join-Path $BaseDirectory $RelativePath))
    $prefix = [IO.Path]::GetFullPath($BaseDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "Missing artifact or path outside its owning directory: $RelativePath."
    }
    return $fullPath
}

foreach ($relativePath in @('.ainetcodenavigator-assembly-export', 'README.md', 'assemblies.json', 'last-run.log')) {
    $null = Assert-DumpFile $dumpDirectory $relativePath
}
$runLog = Get-Content -LiteralPath (Join-Path $dumpDirectory 'last-run.log')
if ($runLog[-1] -notmatch '^RUN COMPLETE exported=1 partial=([01]) failed=0$') {
    throw "Expected one published export and no failures; final run log line: $($runLog[-1])"
}
$partialCount = [int]$Matches[1]
$manifestFiles = @(Get-ChildItem -LiteralPath $dumpDirectory -Filter 'export-manifest.json' -File -Recurse)
if ($manifestFiles.Count -ne 1) { throw "Expected one published manifest, found $($manifestFiles.Count)." }
$manifest = Get-Content -LiteralPath $manifestFiles[0].FullName -Raw | ConvertFrom-Json
if ($manifest.identity.name -ne 'Serilog' -or $manifest.completionState -notin @('complete', 'partial')) {
    throw 'Expected a published Serilog export with complete or partial state.'
}
$expectedPartialCount = if ($manifest.completionState -eq 'partial') { 1 } else { 0 }
if ($partialCount -ne $expectedPartialCount) { throw 'Run log partial count differs from the manifest state.' }
$diagnostics = @($manifest.diagnostics.referenceClosure) + @($manifest.diagnostics.decompilation)
if ($expectedPartialCount -eq 0 -and $diagnostics.Count -gt 0) {
    throw 'Complete Serilog output must not contain diagnostics.'
}
$childDirectory = [IO.Path]::GetDirectoryName($manifestFiles[0].FullName)
$expectedManifestPath = Assert-DumpFile $dumpDirectory ($manifest.childRelativePath + '/export-manifest.json')
if ($expectedManifestPath -ne $manifestFiles[0].FullName) { throw 'Manifest childRelativePath does not identify its published directory.' }
$null = Assert-DumpFile $childDirectory $manifest.projectPath
$null = Assert-DumpFile $childDirectory ([IO.Path]::ChangeExtension($manifest.projectPath, '.sln'))
$sourceFiles = @($manifest.sourceFiles)
if ($sourceFiles.Count -eq 0) { throw 'Manifest has no C# source files.' }
foreach ($sourceFile in $sourceFiles) { $null = Assert-DumpFile $childDirectory $sourceFile }
$searchableFile = @($sourceFiles | Where-Object { [IO.Path]::GetFileName($_) -eq 'LogEvent.cs' })
if ($searchableFile.Count -ne 1) { throw 'Expected one listed LogEvent.cs file for targeted navigation.' }
$searchablePath = Assert-DumpFile $childDirectory $searchableFile[0]
if (-not (Select-String -LiteralPath $searchablePath -Pattern '\bclass\s+LogEvent\b' -Quiet)) {
    throw 'LogEvent.cs must contain a readable LogEvent class declaration.'
}

$catalog = Get-Content -LiteralPath (Join-Path $dumpDirectory 'assemblies.json') -Raw | ConvertFrom-Json
if ($catalog.schemaVersion -ne 1 -or $catalog.runState -ne 'complete' -or
    $catalog.runId -ne $manifest.runId -or @($catalog.rows).Count -ne 1 -or
    $catalog.selected -ne 1 -or $catalog.exported -ne 1 -or
    $catalog.partial -ne $partialCount -or $catalog.failed -ne 0) {
    throw 'Catalog must contain exactly the published assembly from this run.'
}
$row = $catalog.rows[0]
foreach ($field in @('name', 'version', 'culture', 'publicKeyToken', 'childRelativePath', 'completionState')) {
    $column = [Array]::IndexOf([string[]]$catalog.columns, $field)
    if ($column -lt 0) { throw "Catalog is missing column '$field'." }
    $expected = if ($field -in @('childRelativePath', 'completionState')) { $manifest.$field } else { $manifest.identity.$field }
    if ($row[$column] -ne $expected) { throw "Catalog field '$field' differs from the manifest." }
}

Write-Host "[PASS] Navigation artifacts agree; $($sourceFiles.Count) C# files, assembly state=$($manifest.completionState)."
Write-Host "[PASS] Targeted declaration search: $($searchableFile[0]) -> class LogEvent"
if ($diagnostics.Count -gt 0) {
    Write-Host "[NOTE] Partial output: $($diagnostics.Count) recorded limitations; read export-manifest.json before relying on source completeness."
    $diagnostics | Select-Object -First 3 | ForEach-Object {
        $message = ($_.message -replace '\s+', ' ').Trim()
        if ($message.Length -gt 240) { $message = $message.Substring(0, 237) + '...' }
        Write-Host "  $($_.code): $message"
    }
} elseif ($manifest.completionState -eq 'partial') {
    Write-Host '[NOTE] Partial output without diagnostic entries; inspect manifest dependencies and completionState before relying on source completeness.'
}
Write-Host "[INFO] Dump: $dumpDirectory"
Write-Host '[INFO] Representative source paths:'
$sourceFiles | Where-Object { (Get-Item -LiteralPath (Join-Path $childDirectory $_)).Length -gt 0 } |
    Sort-Object | Select-Object -First 8 | ForEach-Object { Write-Host "  $_" }
exit 0
