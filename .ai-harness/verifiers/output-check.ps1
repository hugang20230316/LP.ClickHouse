param(
    [Parameter(Mandatory)][string]$WorkspaceRoot,
    [Parameter(Mandatory)][string]$ReportRoot,
    [Parameter(Mandatory)]$Task,
    [Parameter(Mandatory)]$Verifier
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path -Path (Split-Path -Path $PSScriptRoot -Parent) -ChildPath 'runner\Harness.Common.ps1')

$paths = if ($Verifier.PSObject.Properties.Name.Contains('paths')) {
    @($Verifier.paths)
}
else {
    @($Task.expected_artifacts)
}

if (@($paths).Count -eq 0) {
    throw "output-check verifier 需要 `paths` 或 `expected_artifacts` 至少填 1 项。"
}

$missing = @()
foreach ($path in @($paths)) {
    $fullPath = Resolve-HarnessPath -BasePath $WorkspaceRoot -RelativePath $path
    if (-not (Test-Path -LiteralPath $fullPath)) {
        $missing += $path
    }
}

if (@($missing).Count -gt 0) {
    return [ordered]@{
        name        = $Verifier.name
        type        = $Verifier.type
        status      = 'failed'
        exit_code   = 1
        duration_ms = 0
        message     = "缺少预期产物：$($missing -join ', ')"
    }
}

return [ordered]@{
    name        = $Verifier.name
    type        = $Verifier.type
    status      = 'passed'
    exit_code   = 0
    duration_ms = 0
    message     = '产物检查通过'
}
