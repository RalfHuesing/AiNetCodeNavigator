#requires -Version 7.0
<#
.SYNOPSIS
    Exports a generated managed probe DLL and checks agent navigation artifacts.

.DESCRIPTION
    Builds through scripts/build.ps1 unless -SkipBuild is supplied. A small managed
    probe assembly is generated under temp. The exporter replaces its dedicated,
    marked temp/assembly-export-smoke-dump directory on every run; an existing
    unmarked directory is rejected by the exporter.

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
if (-not (Test-Path -LiteralPath $exporter -PathType Leaf)) {
    throw "Missing built exporter: $exporter. Run this script without -SkipBuild."
}

$probeDirectory = Join-Path $repoRoot 'temp/assembly-export-smoke-probe'
$null = New-Item -ItemType Directory -Path $probeDirectory -Force
$assembly = Join-Path $probeDirectory 'AssemblyExportSmokeProbe.dll'
if (Test-Path -LiteralPath $assembly) { Remove-Item -LiteralPath $assembly -Force }
$probeSource = @'
namespace SmokeFixture
{
    public sealed class ProbeRecord
    {
        public string Marker => "assembly-export-smoke";
    }
}
'@
Add-Type -TypeDefinition $probeSource -Language CSharp -OutputAssembly $assembly -OutputType Library
if (-not (Test-Path -LiteralPath $assembly -PathType Leaf)) {
    throw "Failed to create managed probe assembly: $assembly"
}
$probeAssemblyName = [Reflection.AssemblyName]::GetAssemblyName($assembly).Name
$excludedProbe = Join-Path $probeDirectory 'DevExpressSmokeProbe.dll'
Copy-Item -LiteralPath $assembly -Destination $excludedProbe -Force

$dumpDirectory = Join-Path $repoRoot 'temp/assembly-export-smoke-dump'
Write-Host "[INFO] Exporting managed probe to $dumpDirectory"
& $exporter --output $dumpDirectory --source $probeDirectory --include '*Probe.dll' --exclude 'devexpress*.DLL' --dependencies none
if ($LASTEXITCODE -ne 0) { throw "Assembly export failed with exit code $LASTEXITCODE. See temp/assembly-export-smoke-dump/last-run.log." }

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

foreach ($relativePath in @('.ainetcodenavigator-assembly-export', 'README.md', 'assemblies.json', 'last-run.log', 'namespace-map.md', 'symbol-map.md')) {
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
if ($manifest.schemaVersion -ne 2 -or $manifest.identity.name -ne $probeAssemblyName -or
    $manifest.completionState -notin @('complete', 'partial')) {
    throw 'Expected the managed probe export with complete or partial state.'
}
$expectedPartialCount = if ($manifest.completionState -eq 'partial') { 1 } else { 0 }
if ($partialCount -ne $expectedPartialCount) { throw 'Run log partial count differs from the manifest state.' }
$childDirectory = [IO.Path]::GetDirectoryName($manifestFiles[0].FullName)
$expectedManifestPath = Assert-DumpFile $dumpDirectory ($manifest.childRelativePath + '/export-manifest.json')
if ($expectedManifestPath -ne $manifestFiles[0].FullName) { throw 'Manifest childRelativePath does not identify its published directory.' }
$null = Assert-DumpFile $childDirectory $manifest.projectPath
$null = Assert-DumpFile $childDirectory ([IO.Path]::ChangeExtension($manifest.projectPath, '.sln'))
$sourceListPath = Assert-DumpFile $childDirectory $manifest.sourceFilesPath
$sourceFiles = @(Get-Content -LiteralPath $sourceListPath -Raw | ConvertFrom-Json)
if ($sourceFiles.Count -eq 0) { throw 'Manifest has no C# source files.' }
if ($manifest.counts.sourceFiles -ne $sourceFiles.Count) { throw 'Source count differs from source-files.json.' }
foreach ($sourceFile in $sourceFiles) { $null = Assert-DumpFile $childDirectory $sourceFile }
$dependencyDetails = [pscustomobject]@{ dependencies = @(); filteredEdges = @() }
if ($manifest.dependenciesPath) {
    $dependencyDetails = Get-Content -LiteralPath (Assert-DumpFile $childDirectory $manifest.dependenciesPath) -Raw | ConvertFrom-Json
}
if ($manifest.counts.dependencies -ne @($dependencyDetails.dependencies).Count -or
    $manifest.counts.filteredEdges -ne @($dependencyDetails.filteredEdges).Count) {
    throw 'Dependency counts differ from dependencies.json.'
}
$diagnosticDetails = [pscustomobject]@{ referenceClosure = @(); decompilation = @() }
if ($manifest.diagnosticsPath) {
    $diagnosticDetails = Get-Content -LiteralPath (Assert-DumpFile $childDirectory $manifest.diagnosticsPath) -Raw | ConvertFrom-Json
}
$diagnostics = @($diagnosticDetails.referenceClosure) + @($diagnosticDetails.decompilation)
if ($manifest.counts.referenceClosureDiagnostics -ne @($diagnosticDetails.referenceClosure).Count -or
    $manifest.counts.decompilationDiagnostics -ne @($diagnosticDetails.decompilation).Count) {
    throw 'Diagnostic counts differ from diagnostics.json.'
}
if ($expectedPartialCount -eq 0 -and $diagnostics.Count -gt 0) {
    throw 'Complete output must not contain diagnostics.'
}
$searchableFile = @($sourceFiles | Where-Object { [IO.Path]::GetFileName($_) -eq 'ProbeRecord.cs' })
if ($searchableFile.Count -ne 1) { throw 'Expected one listed ProbeRecord.cs file for targeted navigation.' }
$searchablePath = Assert-DumpFile $childDirectory $searchableFile[0]
if (-not (Select-String -LiteralPath $searchablePath -Pattern '\bclass\s+ProbeRecord\b' -Quiet)) {
    throw 'ProbeRecord.cs must contain a readable ProbeRecord class declaration.'
}
$mappedSource = ($manifest.childRelativePath + '/' + $searchableFile[0]).Replace('\', '/')
$expectedMapEntry = '- ``SmokeFixture.ProbeRecord`` -> ``' + $mappedSource + '``'
foreach ($mapName in @('namespace-map.md', 'symbol-map.md')) {
    $mapPath = Assert-DumpFile $dumpDirectory $mapName
    $mapLines = @(Get-Content -LiteralPath $mapPath)
    if (@($mapLines | Where-Object { $_ -ceq $expectedMapEntry }).Count -ne 1) {
        throw "Expected exactly one ProbeRecord symbol-to-source entry in $mapName."
    }
    $null = Assert-DumpFile $dumpDirectory $mappedSource
    if ($mapName -eq 'namespace-map.md' -and '## SmokeFixture' -cnotin $mapLines) {
        throw 'Namespace map must group ProbeRecord under its declared namespace.'
    }
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
    $expected = if ($field -eq 'name') { $probeAssemblyName }
        elseif ($field -in @('childRelativePath', 'completionState')) { $manifest.$field }
        else { $manifest.identity.$field }
    if ($row[$column] -ne $expected) { throw "Catalog field '$field' differs from the manifest." }
}

Write-Host "[PASS] Navigation artifacts agree; $($sourceFiles.Count) C# files, assembly state=$($manifest.completionState)."
Write-Host "[PASS] Targeted declaration search: $($searchableFile[0]) -> class ProbeRecord"

$dumpSnapshot = @(Get-ChildItem -LiteralPath $dumpDirectory -File -Recurse | Sort-Object FullName | ForEach-Object {
    $_.FullName + ':' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
})
Write-Host '[INFO] Checking dry-run selection and exclusions without changing the dump'
$preview = @(& $exporter --output $dumpDirectory --source $probeDirectory --include '*Probe.dll' --exclude 'devexpress*.DLL' --dry-run)
if ($LASTEXITCODE -ne 0) { throw "Dry-run failed with exit code $LASTEXITCODE." }
$previewText = $preview -join "`n"
if ($previewText -notmatch 'Selected.*AssemblyExportSmokeProbe.dll' -or
    $previewText -notmatch 'Excluded.*DevExpressSmokeProbe.dll.*exclude:devexpress\*.DLL') {
    throw "Dry-run did not identify the selected and excluded probe assemblies: $previewText"
}
$afterPreview = @(Get-ChildItem -LiteralPath $dumpDirectory -File -Recurse | Sort-Object FullName | ForEach-Object {
    $_.FullName + ':' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
})
if (@(Compare-Object $dumpSnapshot $afterPreview).Count -ne 0) { throw 'Dry-run modified the existing dump.' }
Write-Host '[PASS] Named selection, case-insensitive exclusions, and dry-run dump preservation'
if ($diagnostics.Count -gt 0) {
    Write-Host "[NOTE] Partial output: $($diagnostics.Count) recorded limitations; read diagnostics.json before relying on source completeness."
    $diagnostics | Select-Object -First 3 | ForEach-Object {
        $message = ($_.message -replace '\s+', ' ').Trim()
        if ($message.Length -gt 240) { $message = $message.Substring(0, 237) + '...' }
        Write-Host "  $($_.code): $message"
    }
} elseif ($manifest.completionState -eq 'partial') {
    Write-Host '[NOTE] Partial output without diagnostic entries; inspect dependencies.json and completionState before relying on source completeness.'
}
Write-Host "[INFO] Dump: $dumpDirectory"
Write-Host '[INFO] Representative source paths:'
$sourceFiles | Where-Object { (Get-Item -LiteralPath (Join-Path $childDirectory $_)).Length -gt 0 } |
    Sort-Object | Select-Object -First 8 | ForEach-Object { Write-Host "  $_" }
exit 0
