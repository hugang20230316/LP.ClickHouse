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

$taskConfigPath = Join-Path -Path $TaskRoot -ChildPath 'task.yaml'
$manifestPath = Join-Path -Path $TaskRoot -ChildPath 'fixtures\manifest.json'

if (-not (Test-Path -LiteralPath $taskConfigPath)) {
    throw "缺少 task.yaml：$taskConfigPath"
}

if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw "缺少 fixture manifest：$manifestPath"
}

$task = Get-Content -LiteralPath $taskConfigPath -Raw -Encoding utf8 | ConvertFrom-Json -Depth 20
if ([string]$task.execution_mode -ne 'agent') {
    throw "HARNESS-002 仅支持 execution_mode=agent，当前值：$($task.execution_mode)"
}

if (-not (Get-Command codex -ErrorAction SilentlyContinue)) {
    throw "未找到 codex 命令，请先安装 Codex CLI。"
}

$loginOutput = & codex login status 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "Codex 登录状态检查失败：$loginOutput"
}

if (($loginOutput | Out-String) -notmatch 'Logged in') {
    throw "Codex 未登录，请先执行 codex login。"
}

foreach ($path in @($task.allowed_paths)) {
    $fullPath = Join-Path -Path $WorkspaceRoot -ChildPath $path
    if (-not (Test-Path -LiteralPath $fullPath)) {
        throw "allowed_paths 对应文件不存在：$path"
    }
}

Write-Host "HARNESS-002 setup completed. CodexReady=true"
