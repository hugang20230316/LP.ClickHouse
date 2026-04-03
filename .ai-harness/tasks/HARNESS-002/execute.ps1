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

function Convert-GlobToRegex {
    param([Parameter(Mandatory)][string]$Pattern)

    $normalized = ($Pattern -replace '\\', '/')
    $escaped = [regex]::Escape($normalized)
    $escaped = $escaped -replace '\\\*\\\*', '.*'
    $escaped = $escaped -replace '\\\*', '[^/]*'
    $escaped = $escaped -replace '\\\?', '.'
    return '^{0}$' -f $escaped
}

function Test-PathMatch {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string[]]$Patterns
    )

    $normalized = ($Path -replace '\\', '/')
    foreach ($pattern in $Patterns) {
        if ([string]::IsNullOrWhiteSpace($pattern)) {
            continue
        }

        if ($normalized -match (Convert-GlobToRegex -Pattern $pattern)) {
            return $true
        }
    }

    return $false
}

function ConvertTo-Hash {
    param([Parameter(Mandatory)][string]$Text)

    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
    try {
        return ([System.BitConverter]::ToString($sha256.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $sha256.Dispose()
    }
}

function Get-AgentFileEntries {
    param([Parameter(Mandatory)][string]$RawContent)

    try {
        $data = $RawContent | ConvertFrom-Json -Depth 20
    }
    catch {
        throw "模型输出不是合法 JSON：$($_.Exception.Message)"
    }

    if (-not $data -or -not $data.PSObject.Properties.Name.Contains('files')) {
        throw '模型输出缺少 files 字段。'
    }

    $entries = @($data.files)
    if ($entries.Count -eq 0) {
        throw '模型输出的 files 为空，无法生成改动。'
    }

    return $entries
}

function Write-Utf8FileNoBom {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Content
    )

    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Content, $encoding)
}

function Write-AgentResult {
    param(
        [Parameter(Mandatory)]$Result,
        [Parameter(Mandatory)][string]$Path
    )

    $Result | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $Path -Encoding utf8NoBOM
}

$startedAt = Get-Date
$taskConfigPath = Join-Path -Path $TaskRoot -ChildPath 'task.yaml'
$task = Get-Content -LiteralPath $taskConfigPath -Raw -Encoding utf8 | ConvertFrom-Json -Depth 20

if ([string]$task.execution_mode -ne 'agent') {
    throw "HARNESS-002 execute 仅支持 execution_mode=agent，当前值：$($task.execution_mode)"
}

$agent = $task.agent
if (-not $agent) {
    throw 'task.yaml 缺少 agent 配置。'
}

$provider = if ([string]::IsNullOrWhiteSpace([string]$agent.provider)) { 'codex' } else { [string]$agent.provider }
if ($provider -ne 'codex') {
    throw "HARNESS-002 v1 仅支持 codex provider，当前值：$provider"
}

$model = if ([string]::IsNullOrWhiteSpace([string]$agent.model)) { 'gpt-5.4' } else { [string]$agent.model }
$reasoningEffort = if ([string]::IsNullOrWhiteSpace([string]$agent.reasoning_effort)) { 'xhigh' } else { [string]$agent.reasoning_effort }
$promptVersion = if ([string]::IsNullOrWhiteSpace([string]$agent.prompt_version)) { 'v1' } else { [string]$agent.prompt_version }
$maxAttempts = if ($agent.PSObject.Properties.Name.Contains('max_attempts')) { [Math]::Max(1, [int]$agent.max_attempts) } else { 1 }
$timeoutSec = if ($agent.PSObject.Properties.Name.Contains('timeout_sec')) { [int]$agent.timeout_sec } else { [int]$task.timeout_sec }
$contextMaxBytes = if ($agent.PSObject.Properties.Name.Contains('context_max_bytes')) { [int]$agent.context_max_bytes } else { 200000 }

if ($timeoutSec -le 0) {
    throw "agent.timeout_sec 必须大于 0，当前值：$timeoutSec"
}

$allowedPaths = @($task.allowed_paths)
$forbiddenPaths = @($task.forbidden_paths)
$contextPaths = if ($agent.PSObject.Properties.Name.Contains('context_paths') -and @($agent.context_paths).Count -gt 0) { @($agent.context_paths) } else { $allowedPaths }
$artifactsRoot = Join-Path -Path $ReportRoot -ChildPath 'artifacts\agent'
New-Item -ItemType Directory -Path $artifactsRoot -Force | Out-Null

$promptPath = Join-Path -Path $artifactsRoot -ChildPath 'prompt.txt'
$promptMetaPath = Join-Path -Path $artifactsRoot -ChildPath 'prompt.meta.json'
$schemaPath = Join-Path -Path $artifactsRoot -ChildPath 'output.schema.json'
$rawPath = Join-Path -Path $artifactsRoot -ChildPath 'agent.raw.txt'
$stdoutPath = Join-Path -Path $artifactsRoot -ChildPath 'agent.stdout.log'
$stderrPath = Join-Path -Path $artifactsRoot -ChildPath 'agent.stderr.log'
$patchPath = Join-Path -Path $artifactsRoot -ChildPath 'agent.diff.patch'
$changedFilesPath = Join-Path -Path $artifactsRoot -ChildPath 'changed-files.json'
$resultPath = Join-Path -Path $artifactsRoot -ChildPath 'agent.result.json'

Set-Content -LiteralPath $stdoutPath -Value '' -Encoding utf8NoBOM
Set-Content -LiteralPath $stderrPath -Value '' -Encoding utf8NoBOM

$contextBuilder = New-Object System.Text.StringBuilder
$totalBytes = 0
foreach ($relativePath in $contextPaths) {
    $pathText = [string]$relativePath
    $normalizedPath = ($pathText -replace '\\', '/')
    if ($normalizedPath -match '[*?]') {
        throw "v1 不支持 context_paths 使用通配符，请改为显式路径：$pathText"
    }

    $fullPath = Join-Path -Path $WorkspaceRoot -ChildPath $pathText
    if (-not (Test-Path -LiteralPath $fullPath)) {
        throw "上下文文件不存在：$pathText"
    }

    $content = Get-Content -LiteralPath $fullPath -Raw -Encoding utf8
    $contentBytes = [System.Text.Encoding]::UTF8.GetByteCount($content)
    $totalBytes += $contentBytes
    if ($totalBytes -gt $contextMaxBytes) {
        throw "上下文总字节超过上限（$contextMaxBytes），请缩小 context_paths。"
    }

    [void]$contextBuilder.AppendLine("### FILE: $normalizedPath")
    [void]$contextBuilder.AppendLine($content)
    [void]$contextBuilder.AppendLine("### END FILE: $normalizedPath")
    [void]$contextBuilder.AppendLine('')
}

$allowedText = (($allowedPaths | ForEach-Object { "- $($_ -replace '\\', '/')" }) -join "`n")
$forbiddenText = (($forbiddenPaths | ForEach-Object { "- $($_ -replace '\\', '/')" }) -join "`n")
$goal = [string]$task.goal

$prompt = @"
你是资深 C# 工程师。你已经拿到了完成任务所需的全部上下文，禁止再读取任何文件、禁止运行任何 shell/git/dotnet 命令、禁止做环境探查。只允许基于下面给出的代码上下文完成修改。

任务目标：
$goal

硬约束：
1. 只允许修改 allowed_paths 中的文件。
2. 禁止修改 forbidden_paths 中的文件。
3. 额外禁止修改 .git/** 与 .ai-harness/**。
4. 禁止运行任何命令；所有判断只能基于本提示中已经给出的上下文。
5. 只返回“发生变化的文件”，不要返回未改动文件。
6. 每个返回项都必须包含文件相对路径和修改后的完整文件内容。
7. 返回内容必须严格匹配输出 schema，不要额外解释。

allowed_paths：
$allowedText

forbidden_paths：
$forbiddenText

当前代码上下文：
$($contextBuilder.ToString())
"@

$basePrompt = $prompt
Set-Content -LiteralPath $promptPath -Value $basePrompt -Encoding utf8NoBOM

$outputSchema = [ordered]@{
    type = 'object'
    properties = [ordered]@{
        files = [ordered]@{
            type = 'array'
            minItems = 1
            items = [ordered]@{
                type = 'object'
                properties = [ordered]@{
                    path = [ordered]@{
                        type = 'string'
                        enum = @($allowedPaths | ForEach-Object { $_ -replace '\\', '/' })
                    }
                    content = [ordered]@{
                        type = 'string'
                    }
                }
                required = @('path', 'content')
                additionalProperties = $false
            }
        }
    }
    required = @('files')
    additionalProperties = $false
}
Write-Utf8FileNoBom -Path $schemaPath -Content ($outputSchema | ConvertTo-Json -Depth 20)

$promptMeta = [ordered]@{
    provider            = $provider
    model               = $model
    reasoning_effort    = $reasoningEffort
    prompt_version      = $promptVersion
    output_mode         = if ($agent.PSObject.Properties.Name.Contains('output_mode')) { [string]$agent.output_mode } else { 'file-map' }
    execution_mode      = [string]$task.execution_mode
    context_total_bytes = $totalBytes
    allowed_paths_hash  = ConvertTo-Hash -Text (($allowedPaths -join "`n"))
    forbidden_paths_hash = ConvertTo-Hash -Text (($forbiddenPaths -join "`n"))
}
$promptMeta | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $promptMetaPath -Encoding utf8NoBOM

$pwshPath = if (Test-Path 'C:\Program Files\PowerShell\7\pwsh.exe') { 'C:\Program Files\PowerShell\7\pwsh.exe' } else { 'pwsh' }
$runId = Split-Path -Path $ReportRoot -Leaf

$result = [ordered]@{
    run_id            = $runId
    task_id           = [string]$task.task_id
    base_commit       = [string]$task.base_commit
    provider          = $provider
    model             = $model
    reasoning_effort  = $reasoningEffort
    prompt_version    = $promptVersion
    attempt_count     = 0
    status            = 'failed'
    exit_code         = 1
    timed_out         = $false
    started_at        = $startedAt.ToString('o')
    ended_at          = $null
    duration_ms       = 0
    session_id        = $null
    error             = $null
}

$attempt = 0
$retryHint = $null
while ($attempt -lt $maxAttempts) {
    $attempt++
    $result.attempt_count = $attempt

    $attemptPrompt = if ([string]::IsNullOrWhiteSpace($retryHint)) {
        $basePrompt
    }
    else {
@"
$basePrompt

上一轮输出未通过 harness 校验，请只修正输出内容本身，不要改变任务目标：
$retryHint
"@
    }

    Write-Utf8FileNoBom -Path $promptPath -Content $attemptPrompt

    Add-Content -LiteralPath $stdoutPath -Value ("`n=== attempt {0} ===`n" -f $attempt) -Encoding utf8
    Add-Content -LiteralPath $stderrPath -Value ("`n=== attempt {0} ===`n" -f $attempt) -Encoding utf8

    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        Push-Location $WorkspaceRoot
        try {
            & codex -c 'mcp_servers={}' -c "model_reasoning_effort=`"$reasoningEffort`"" -a never exec -s read-only -m $model --output-schema $schemaPath -o $rawPath $attemptPrompt 1>> $stdoutPath 2>> $stderrPath
            $exitCode = $LASTEXITCODE
        }
        finally {
            Pop-Location
        }
    }
    catch {
        $watch.Stop()
        $result.timed_out = $false
        $result.duration_ms = [int]$watch.Elapsed.TotalMilliseconds
        $result.exit_code = 1
        $result.error = $_.Exception.Message
        $retryHint = $result.error
        continue
    }
    $watch.Stop()
    $result.duration_ms = [int]$watch.Elapsed.TotalMilliseconds
    $result.timed_out = $false
    $result.exit_code = $exitCode

    if ($result.exit_code -ne 0) {
        $stderrText = if (Test-Path -LiteralPath $stderrPath) { Get-Content -LiteralPath $stderrPath -Raw -Encoding utf8 } else { '' }
        if ($stderrText -match 'login|auth|device code') {
            $result.error = 'Codex 触发登录或鉴权交互，请先完成 codex login 后再试。'
        }
        else {
            $result.error = "Codex 执行失败，exit_code=$($result.exit_code)"
        }
        $retryHint = $result.error
        continue
    }

    $rawContent = ''
    if (Test-Path -LiteralPath $rawPath) {
        $rawContent = Get-Content -LiteralPath $rawPath -Raw -Encoding utf8
    }
    elseif (Test-Path -LiteralPath $stdoutPath) {
        $rawContent = Get-Content -LiteralPath $stdoutPath -Raw -Encoding utf8
    }

    if ([string]::IsNullOrWhiteSpace($rawContent)) {
        $result.error = '未生成可解析的模型输出。'
        $retryHint = $result.error
        continue
    }

    try {
        $entries = @()
        $seenPaths = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
        foreach ($entry in @(Get-AgentFileEntries -RawContent $rawContent)) {
            $path = ([string]$entry.path -replace '\\', '/')
            if ([string]::IsNullOrWhiteSpace($path)) {
                throw '模型返回了空路径。'
            }

            if ([System.IO.Path]::IsPathRooted($path) -or $path -match '(^|/)\.\.(/|$)') {
                throw "模型返回了非法路径：$path"
            }

            if ($path -match '^\.git/' -or $path -match '^\.ai-harness/') {
                throw "模型命中硬禁目录：$path"
            }

            if (Test-PathMatch -Path $path -Patterns $forbiddenPaths) {
                throw "模型命中 forbidden_paths：$path"
            }

            if (-not (Test-PathMatch -Path $path -Patterns $allowedPaths)) {
                throw "模型改动超出 allowed_paths：$path"
            }

            if (-not $seenPaths.Add($path)) {
                throw "模型输出存在重复路径：$path"
            }

            $fullPath = Join-Path -Path $WorkspaceRoot -ChildPath $path
            if (-not (Test-Path -LiteralPath $fullPath)) {
                throw "目标文件不存在：$path"
            }

            Write-Utf8FileNoBom -Path $fullPath -Content ([string]$entry.content)
            $entries += [ordered]@{
                path = $path
            }
        }

        $entries | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $changedFilesPath -Encoding utf8NoBOM
    }
    catch {
        $result.error = $_.Exception.Message
        $retryHint = $result.error
        continue
    }

    $changedFiles = @(
        (& git -C $WorkspaceRoot diff --name-only --relative HEAD 2>&1) |
            Where-Object { $_ -and ($_ -notmatch '^warning: in the working copy of ') }
    )

    if ($changedFiles.Count -eq 0) {
        $result.error = '模型输出写回后未产生任何改动。'
        $retryHint = $result.error
        continue
    }

    $normalizedChanged = @($changedFiles | ForEach-Object { $_ -replace '\\', '/' })
    if (@($normalizedChanged | Where-Object { -not (Test-PathMatch -Path $_ -Patterns $allowedPaths) }).Count -gt 0) {
        $result.error = "写回后的 git diff 超出 allowed_paths：$($normalizedChanged -join ', ')"
        $retryHint = $result.error
        continue
    }

    $normalizedChanged | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $changedFilesPath -Encoding utf8NoBOM

    $patchLines = @(
        (& git -C $WorkspaceRoot diff --relative HEAD 2>&1) |
            Where-Object { $_ -and ($_ -notmatch '^warning: in the working copy of ') }
    )
    $patchContent = ($patchLines | Out-String)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($patchContent)) {
        $result.error = '无法根据模型输出生成标准 diff。'
        $retryHint = $result.error
        continue
    }

    Set-Content -LiteralPath $patchPath -Value $patchContent -Encoding utf8NoBOM

    $sessionSource = ''
    if (Test-Path -LiteralPath $stderrPath) {
        $sessionSource += Get-Content -LiteralPath $stderrPath -Raw -Encoding utf8
    }

    if (Test-Path -LiteralPath $stdoutPath) {
        $sessionSource += "`n" + (Get-Content -LiteralPath $stdoutPath -Raw -Encoding utf8)
    }

    $sessionMatch = [regex]::Match($sessionSource, 'session id:\s*([0-9a-fA-F\-]+)')
    if ($sessionMatch.Success) {
        $result.session_id = $sessionMatch.Groups[1].Value
    }

    $result.status = 'passed'
    $result.exit_code = 0
    $result.error = $null
    break
}

$endedAt = Get-Date
$result.ended_at = $endedAt.ToString('o')
$result.duration_ms = [int]($endedAt - $startedAt).TotalMilliseconds
Write-AgentResult -Result $result -Path $resultPath

if ($result.status -ne 'passed') {
    throw $result.error
}

Write-Host "HARNESS-002 execute completed. Provider=$provider Model=$model Attempts=$($result.attempt_count)"
