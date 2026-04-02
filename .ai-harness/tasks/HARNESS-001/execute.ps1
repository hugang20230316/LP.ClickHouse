param(
    [Parameter(Mandatory)]
    [string]$WorkspaceRoot,

    [Parameter(Mandatory)]
    [string]$TaskRoot,

    [Parameter(Mandatory)]
    [string]$ReportRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$fixtureRoot = Join-Path -Path $TaskRoot -ChildPath 'fixtures\workspace'
$artifactRoot = Join-Path -Path $ReportRoot -ChildPath 'artifacts'
if (-not (Test-Path -LiteralPath (Join-Path -Path $TaskRoot -ChildPath 'fixtures\manifest.json'))) {
    throw "找不到 fixture manifest：$TaskRoot\fixtures\manifest.json"
}

function Get-FixtureRelativePaths {
    param([string]$ManifestPath)

    $manifest = Get-Content -LiteralPath $ManifestPath -Raw -Encoding utf8 | ConvertFrom-Json -Depth 20
    return @($manifest.files) | ForEach-Object {
        ($_ -replace '^fixtures[/\\]workspace[/\\]?', '') -replace '/', '\'
    }
}

$files = Get-FixtureRelativePaths -ManifestPath (Join-Path -Path $TaskRoot -ChildPath 'fixtures\manifest.json')

foreach ($file in $files) {
    $sourcePath = Join-Path -Path $fixtureRoot -ChildPath $file
    $targetPath = Join-Path -Path $WorkspaceRoot -ChildPath $file
    Copy-Item -LiteralPath $sourcePath -Destination $targetPath -Force
}

if (-not (Test-Path -LiteralPath $artifactRoot)) {
    New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
}

$summaryPath = Join-Path -Path $artifactRoot -ChildPath 'harness-001-summary.json'
$summary = [ordered]@{
    task_id = 'HARNESS-001'
    applied_files = $files
}

$summary | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $summaryPath -Encoding utf8NoBOM
Write-Host "HARNESS-001 execute completed. AppliedFiles=$($files.Count)"


