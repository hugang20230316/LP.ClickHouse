Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:HarnessPwsh = if (Test-Path 'C:\Program Files\PowerShell\7\pwsh.exe') {
    'C:\Program Files\PowerShell\7\pwsh.exe'
}
else {
    'pwsh'
}

function Ensure-HarnessDirectory {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Read-HarnessJsonFile {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "未找到 JSON 文件：$Path"
    }

    $content = Get-Content -LiteralPath $Path -Raw -Encoding utf8
    try {
        return $content | ConvertFrom-Json -Depth 20
    }
    catch {
        throw "当前 v1 任务文件使用 JSON-compatible YAML 写法，请直接写 JSON：$Path"
    }
}

function Write-HarnessJsonFile {
    param(
        [Parameter(Mandatory)]$Data,
        [Parameter(Mandatory)][string]$Path
    )

    Ensure-HarnessDirectory -Path (Split-Path -Path $Path -Parent)
    Set-Content -LiteralPath $Path -Value ($Data | ConvertTo-Json -Depth 20) -Encoding utf8NoBOM
}

function Resolve-HarnessPath {
    param(
        [Parameter(Mandatory)][string]$BasePath,
        [Parameter(Mandatory)][string]$RelativePath
    )

    if ([System.IO.Path]::IsPathRooted($RelativePath)) {
        return $RelativePath
    }

    return [System.IO.Path]::GetFullPath((Join-Path -Path $BasePath -ChildPath $RelativePath))
}

function Read-TaskConfig {
    param([Parameter(Mandatory)][string]$TaskConfigPath)

    $task = Read-HarnessJsonFile -Path $TaskConfigPath
    $required = @(
        'task_id',
        'schema_version',
        'title',
        'goal',
        'repo',
        'base_commit',
        'setup_script',
        'execution_script',
        'allowed_paths',
        'forbidden_paths',
        'fixtures_manifest',
        'verifiers',
        'expected_artifacts',
        'timeout_sec'
    )

    foreach ($field in $required) {
        if (-not $task.PSObject.Properties.Name.Contains($field)) {
            throw "task.yaml 缺少字段：$field"
        }
    }

    foreach ($field in @('task_id', 'title', 'goal', 'repo', 'base_commit', 'setup_script', 'execution_script', 'fixtures_manifest')) {
        if ([string]::IsNullOrWhiteSpace([string]$task.$field)) {
            throw "task.yaml 字段不能为空：$field"
        }
    }

    $repo = [string]$task.repo
    if ($repo -ne '.') {
        throw "task.yaml 的 repo 目前仅允许 '.'：$repo"
    }

    foreach ($field in @('setup_script', 'execution_script', 'fixtures_manifest')) {
        $value = [string]$task.$field
        if ([System.IO.Path]::IsPathRooted($value)) {
            throw "task.yaml 字段禁止使用绝对路径：$field=$value"
        }

        if (($value -replace '\\', '/') -match '(^|/)\.\.(/|$)') {
            throw "task.yaml 字段禁止包含上跳路径：$field=$value"
        }
    }

    if (@($task.allowed_paths).Count -eq 0) {
        throw 'task.yaml 至少需要 1 条 allowed_paths。'
    }

    if (@($task.verifiers).Count -eq 0) {
        throw 'task.yaml 至少需要 1 个 verifier。'
    }

    if (@($task.expected_artifacts).Count -eq 0) {
        throw 'task.yaml 至少需要 1 个 expected_artifacts。'
    }

    foreach ($path in @($task.expected_artifacts)) {
        $pathValue = [string]$path
        if ([System.IO.Path]::IsPathRooted($pathValue)) {
            throw "expected_artifacts 禁止使用绝对路径：$pathValue"
        }

        if (($pathValue -replace '\\', '/') -match '(^|/)\.\.(/|$)') {
            throw "expected_artifacts 禁止包含上跳路径：$pathValue"
        }
    }

    if ([int]$task.timeout_sec -le 0) {
        throw "task.yaml 的 timeout_sec 必须大于 0：$($task.timeout_sec)"
    }

    return $task
}

function Invoke-HarnessCommand {
    param(
        [Parameter(Mandatory)][string]$Command,
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [Parameter(Mandatory)][string]$ReportRoot,
        [Parameter(Mandatory)][string]$StepName,
        [Parameter(Mandatory)][int]$TimeoutSec
    )

    if (-not (Test-Path -LiteralPath $WorkingDirectory)) {
        throw "命令工作目录不存在：$WorkingDirectory"
    }

    if ($TimeoutSec -le 0) {
        throw "TimeoutSec 必须大于 0：$TimeoutSec"
    }

    $stdoutPath = Join-Path -Path $ReportRoot -ChildPath 'stdout.log'
    $stderrPath = Join-Path -Path $ReportRoot -ChildPath 'stderr.log'
    $tmpOut = Join-Path -Path $env:TEMP -ChildPath ('{0}-{1}-out.log' -f $StepName, [guid]::NewGuid().ToString('N'))
    $tmpErr = Join-Path -Path $env:TEMP -ChildPath ('{0}-{1}-err.log' -f $StepName, [guid]::NewGuid().ToString('N'))

    Add-Content -LiteralPath $stdoutPath -Value ("`n=== {0} ===`n{1}`n" -f $StepName, $Command) -Encoding utf8
    Add-Content -LiteralPath $stderrPath -Value ("`n=== {0} ===`n" -f $StepName) -Encoding utf8

    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $process = Start-Process -FilePath $script:HarnessPwsh `
            -ArgumentList @('-NoProfile', '-Command', $Command) `
            -WorkingDirectory $WorkingDirectory `
            -RedirectStandardOutput $tmpOut `
            -RedirectStandardError $tmpErr `
            -PassThru
    }
    catch {
        throw "无法启动命令 '$Command'：$($_.Exception.Message)"
    }

    $timedOut = $false
    try {
        Wait-Process -Id $process.Id -Timeout $TimeoutSec -ErrorAction Stop
    }
    catch {
        $timedOut = $true
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }
    $watch.Stop()
    $process.Refresh()

    if (Test-Path -LiteralPath $tmpOut) {
        Add-Content -LiteralPath $stdoutPath -Value (Read-HarnessTempLogFile -Path $tmpOut) -Encoding utf8
        Remove-HarnessTempLogFile -Path $tmpOut
    }

    if (Test-Path -LiteralPath $tmpErr) {
        Add-Content -LiteralPath $stderrPath -Value (Read-HarnessTempLogFile -Path $tmpErr) -Encoding utf8
        Remove-HarnessTempLogFile -Path $tmpErr
    }

    return [ordered]@{
        exit_code         = if ($timedOut) { 124 } else { $process.ExitCode }
        duration_ms       = [int]$watch.Elapsed.TotalMilliseconds
        timed_out         = $timedOut
        command           = $Command
        working_directory = $WorkingDirectory
    }
}

function Read-HarnessTempLogFile {
    param(
        [Parameter(Mandatory)][string]$Path,
        [int]$RetryCount = 20,
        [int]$RetryDelayMs = 100
    )

    for ($attempt = 1; $attempt -le $RetryCount; $attempt++) {
        try {
            return Get-Content -LiteralPath $Path -Raw -Encoding utf8
        }
        catch {
            if ($attempt -eq $RetryCount) {
                throw "读取临时日志失败：$Path。$($_.Exception.Message)"
            }

            Start-Sleep -Milliseconds $RetryDelayMs
        }
    }

    return ''
}

function Remove-HarnessTempLogFile {
    param(
        [Parameter(Mandatory)][string]$Path,
        [int]$RetryCount = 20,
        [int]$RetryDelayMs = 100
    )

    for ($attempt = 1; $attempt -le $RetryCount; $attempt++) {
        try {
            Remove-Item -LiteralPath $Path -Force -ErrorAction Stop
            return
        }
        catch {
            if ($attempt -eq $RetryCount) {
                throw "删除临时日志失败：$Path。$($_.Exception.Message)"
            }

            Start-Sleep -Milliseconds $RetryDelayMs
        }
    }
}

function Invoke-HarnessScriptStep {
    param(
        [Parameter(Mandatory)][string]$ScriptPath,
        [Parameter(Mandatory)][string]$WorkspaceRoot,
        [Parameter(Mandatory)][string]$TaskRoot,
        [Parameter(Mandatory)][string]$ReportRoot,
        [Parameter(Mandatory)][string]$StepName,
        [Parameter(Mandatory)][int]$TimeoutSec
    )

    if (-not (Test-Path -LiteralPath $ScriptPath)) {
        throw "未找到步骤脚本：$ScriptPath"
    }

    if (-not (Test-Path -LiteralPath $WorkspaceRoot)) {
        throw "命令工作目录不存在：$WorkspaceRoot"
    }

    if ($TimeoutSec -le 0) {
        throw "TimeoutSec 必须大于 0：$TimeoutSec"
    }

    $stdoutPath = Join-Path -Path $ReportRoot -ChildPath 'stdout.log'
    $stderrPath = Join-Path -Path $ReportRoot -ChildPath 'stderr.log'
    $tmpOut = Join-Path -Path $env:TEMP -ChildPath ('{0}-{1}-out.log' -f $StepName, [guid]::NewGuid().ToString('N'))
    $tmpErr = Join-Path -Path $env:TEMP -ChildPath ('{0}-{1}-err.log' -f $StepName, [guid]::NewGuid().ToString('N'))
    $argumentList = @(
        '-NoProfile',
        '-File',
        $ScriptPath,
        '-WorkspaceRoot',
        $WorkspaceRoot,
        '-TaskRoot',
        $TaskRoot,
        '-ReportRoot',
        $ReportRoot
    )
    $command = "`"$ScriptPath`" -WorkspaceRoot `"$WorkspaceRoot`" -TaskRoot `"$TaskRoot`" -ReportRoot `"$ReportRoot`""

    Add-Content -LiteralPath $stdoutPath -Value ("`n=== {0} ===`n{1}`n" -f $StepName, $command) -Encoding utf8
    Add-Content -LiteralPath $stderrPath -Value ("`n=== {0} ===`n" -f $StepName) -Encoding utf8

    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $process = Start-Process -FilePath $script:HarnessPwsh `
            -ArgumentList $argumentList `
            -WorkingDirectory $WorkspaceRoot `
            -RedirectStandardOutput $tmpOut `
            -RedirectStandardError $tmpErr `
            -PassThru
    }
    catch {
        throw "无法启动脚本 '$ScriptPath'：$($_.Exception.Message)"
    }

    $timedOut = $false
    try {
        Wait-Process -Id $process.Id -Timeout $TimeoutSec -ErrorAction Stop
    }
    catch {
        $timedOut = $true
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }
    $watch.Stop()
    $process.Refresh()

    if (Test-Path -LiteralPath $tmpOut) {
        Add-Content -LiteralPath $stdoutPath -Value (Read-HarnessTempLogFile -Path $tmpOut) -Encoding utf8
        Remove-HarnessTempLogFile -Path $tmpOut
    }

    if (Test-Path -LiteralPath $tmpErr) {
        Add-Content -LiteralPath $stderrPath -Value (Read-HarnessTempLogFile -Path $tmpErr) -Encoding utf8
        Remove-HarnessTempLogFile -Path $tmpErr
    }

    return [ordered]@{
        exit_code         = if ($timedOut) { 124 } else { $process.ExitCode }
        duration_ms       = [int]$watch.Elapsed.TotalMilliseconds
        timed_out         = $timedOut
        command           = $command
        working_directory = $WorkspaceRoot
    }
}

function Convert-HarnessGlobToRegex {
    param([Parameter(Mandatory)][string]$Pattern)

    $normalized = ($Pattern -replace '\\', '/')
    $escaped = [regex]::Escape($normalized)
    $escaped = $escaped -replace '\\\*\\\*', '.*'
    $escaped = $escaped -replace '\\\*', '[^/]*'
    $escaped = $escaped -replace '\\\?', '.'
    return '^{0}$' -f $escaped
}

function Test-HarnessPathMatch {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string[]]$Patterns
    )

    $normalized = ($Path -replace '\\', '/')
    foreach ($pattern in $Patterns) {
        if ([string]::IsNullOrWhiteSpace($pattern)) {
            continue
        }

        if ($normalized -match (Convert-HarnessGlobToRegex -Pattern $pattern)) {
            return $true
        }
    }

    return $false
}

function Get-HarnessEnvironmentInfo {
    $dotnetVersion = try {
        (& dotnet --version 2>$null | Out-String).Trim()
    }
    catch {
        ''
    }

    $codexVersion = try {
        (& codex --version 2>$null | Out-String).Trim()
    }
    catch {
        ''
    }

    return [ordered]@{
        platform           = [System.Environment]::OSVersion.VersionString
        powershell_version = $PSVersionTable.PSVersion.ToString()
        dotnet_version     = $dotnetVersion
        codex_version      = $codexVersion
    }
}

function Invoke-GitText {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string[]]$Arguments
    )

    $output = & git -C $RepoRoot @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "git 命令失败：git -C `"$RepoRoot`" $($Arguments -join ' ')`n$output"
    }

    return ($output | Out-String).Trim()
}

function New-HarnessWorkspace {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$Commit,
        [Parameter(Mandatory)][string]$RunId
    )

    $workspaceRoot = Join-Path -Path $env:TEMP -ChildPath ('harness-standard\{0}' -f $RunId)
    Ensure-HarnessDirectory -Path (Split-Path -Path $workspaceRoot -Parent)

    if (Test-Path -LiteralPath $workspaceRoot) {
        Remove-Item -LiteralPath $workspaceRoot -Recurse -Force
    }

    $null = Invoke-GitText -RepoRoot $RepoRoot -Arguments @('worktree', 'add', '--detach', $workspaceRoot, $Commit)
    return $workspaceRoot
}

function Remove-HarnessWorkspace {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$WorkspaceRoot
    )

    if (-not (Test-Path -LiteralPath $WorkspaceRoot)) {
        return
    }

    try {
        $null = Invoke-GitText -RepoRoot $RepoRoot -Arguments @('worktree', 'remove', '--force', $WorkspaceRoot)
    }
    catch {
        Remove-Item -LiteralPath $WorkspaceRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
