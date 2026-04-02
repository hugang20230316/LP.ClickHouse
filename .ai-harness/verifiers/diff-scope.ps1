param(
    [Parameter(Mandatory)][string]$WorkspaceRoot,
    [Parameter(Mandatory)][string]$ReportRoot,
    [Parameter(Mandatory)]$Task,
    [Parameter(Mandatory)]$Verifier
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path -Path (Split-Path -Path $PSScriptRoot -Parent) -ChildPath 'runner\Harness.Common.ps1')

$allowedPaths = @($Task.allowed_paths)
if (@($allowedPaths).Count -eq 0) {
    throw "diff-scope verifier 需要定义 `allowed_paths`。"
}

$forbiddenPaths = @($Task.forbidden_paths)

$trackedResult = & git -C $WorkspaceRoot diff --name-only --relative HEAD 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "git diff 读取变更失败：$trackedResult"
}
$tracked = @(
    $trackedResult |
        Where-Object {
            $_ -and
            $_ -notmatch '^warning: in the working copy of '
        }
)

$untrackedResult = & git -C $WorkspaceRoot ls-files --others --exclude-standard 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "git ls-files 读取未跟踪变更失败：$untrackedResult"
}
$untracked = @(
    $untrackedResult |
        Where-Object {
            $_ -and
            $_ -notmatch '^warning: in the working copy of '
        }
)

$changedPaths = @($tracked + $untracked | Sort-Object -Unique)

foreach ($path in $changedPaths) {
    if (@($forbiddenPaths).Count -gt 0 -and (Test-HarnessPathMatch -Path $path -Patterns $forbiddenPaths)) {
        return [ordered]@{
            name        = $Verifier.name
            type        = $Verifier.type
            status      = 'failed'
            exit_code   = 1
            duration_ms = 0
            message     = "命中禁止改动路径：$path"
        }
    }
}

foreach ($path in $changedPaths) {
    if (-not (Test-HarnessPathMatch -Path $path -Patterns $allowedPaths)) {
        return [ordered]@{
            name        = $Verifier.name
            type        = $Verifier.type
            status      = 'failed'
            exit_code   = 1
            duration_ms = 0
            message     = "改动超出允许范围：$path"
        }
    }
}

return [ordered]@{
    name        = $Verifier.name
    type        = $Verifier.type
    status      = 'passed'
    exit_code   = 0
    duration_ms = 0
    message     = '改动范围通过'
}
