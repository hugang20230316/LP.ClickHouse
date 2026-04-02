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
$manifestPath = Join-Path -Path $TaskRoot -ChildPath 'fixtures\manifest.json'

if (-not (Test-Path -LiteralPath $fixtureRoot)) {
    throw "未找到 fixture 目录：$fixtureRoot"
}

if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw "缺少 manifest：$manifestPath"
}

function Get-FixtureRelativePaths {
    param([string]$ManifestPath)

    $manifest = Get-Content -LiteralPath $ManifestPath -Raw -Encoding utf8 | ConvertFrom-Json -Depth 20
    return @($manifest.files) | ForEach-Object {
        ($_ -replace '^fixtures[/\\]workspace[/\\]?', '') -replace '/', '\'
    }
}

$requiredFiles = Get-FixtureRelativePaths -ManifestPath $manifestPath

foreach ($relative in $requiredFiles) {
    $path = Join-Path -Path $fixtureRoot -ChildPath $relative
    if (-not (Test-Path -LiteralPath $path)) {
        throw "缺少 fixture 文件：$path"
    }
}

Write-Host "HARNESS-001 setup completed. FixtureRoot=$fixtureRoot"
