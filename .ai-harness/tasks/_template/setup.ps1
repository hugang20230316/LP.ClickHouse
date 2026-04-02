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

# TODO: 用真实准备步骤替换此 stub，例如安装依赖、复制 fixture、收集配置等。
Write-Host 'setup step stub - replace with task-specific workspace preparation.'
Write-Host "WorkspaceRoot=$WorkspaceRoot"
Write-Host "TaskRoot=$TaskRoot"
Write-Host "ReportRoot=$ReportRoot"
