param(
    [Parameter(Mandatory)]
    [string]$TaskId,

    [switch]$PromoteBaseline
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path -Path $PSScriptRoot -ChildPath 'Harness.Common.ps1')

function Get-BaselineComparison {
    param(
        $Baseline,
        [Parameter(Mandatory)][string]$ExecutionStatus,
        [Parameter(Mandatory)]$VerifierResults
    )

    if (-not $Baseline) {
        return [ordered]@{
            status               = 'not_found'
            baseline_found       = $false
            baseline_git_sha     = $null
            expected_status      = $null
            actual_status        = $ExecutionStatus
            verifier_differences = @()
        }
    }

    $actualSummary = [ordered]@{}
    foreach ($verifierResult in $VerifierResults) {
        $actualSummary[$verifierResult.name] = $verifierResult.status
    }

    $differences = @()
    if ([string]$Baseline.expected_status -ne $ExecutionStatus) {
        $differences += [ordered]@{
            name     = 'overall_status'
            expected = [string]$Baseline.expected_status
            actual   = $ExecutionStatus
        }
    }

    $baselineVerifierNames = @($Baseline.verifier_summary.PSObject.Properties.Name)
    foreach ($name in $baselineVerifierNames) {
        $actual = if ($actualSummary.Contains($name)) { [string]$actualSummary[$name] } else { $null }
        $expected = [string]$Baseline.verifier_summary.$name

        if ($expected -ne $actual) {
            $differences += [ordered]@{
                name     = $name
                expected = $expected
                actual   = $actual
            }
        }
    }

    foreach ($name in $actualSummary.Keys) {
        if (-not ($baselineVerifierNames -contains $name)) {
            $differences += [ordered]@{
                name     = $name
                expected = $null
                actual   = [string]$actualSummary[$name]
            }
        }
    }

    return [ordered]@{
        status               = if ($differences.Count -eq 0) { 'matched' } else { 'mismatched' }
        baseline_found       = $true
        baseline_git_sha     = $Baseline.baseline_git_sha
        expected_status      = [string]$Baseline.expected_status
        actual_status        = $ExecutionStatus
        verifier_differences = @($differences)
    }
}

$env:DOTNET_CLI_HOME = "$env:TEMP\dotnet-cli-home"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_NOLOGO = '1'

function Resolve-AndValidateTaskScript {
    param(
        [Parameter(Mandatory)][string]$TaskRoot,
        [Parameter(Mandatory)][string]$RelativePath,
        [Parameter(Mandatory)][string]$Label
    )

    if ([string]::IsNullOrWhiteSpace($RelativePath)) {
        throw "$Label script is not configured in task.yaml"
    }

    $path = Resolve-HarnessPath -BasePath $TaskRoot -RelativePath $RelativePath
    if (-not (Test-Path -LiteralPath $path)) {
        throw "$Label script not found: $path"
    }

    return $path
}

function Summarize-StepResult {
    param(
        [Parameter(Mandatory)][string]$StepName,
        [Parameter(Mandatory)]$StepResult
    )

    return [ordered]@{
        step_name      = $StepName
        exit_code      = $StepResult.exit_code
        command        = $StepResult.command
        duration_ms    = $StepResult.duration_ms
        timed_out      = $StepResult.timed_out
        workspace_dir  = $StepResult.working_directory
    }
}

$harnessRoot = Split-Path -Path $PSScriptRoot -Parent
$projectRoot = Split-Path -Path $harnessRoot -Parent
$taskRoot = Join-Path -Path $harnessRoot -ChildPath ("tasks\{0}" -f $TaskId)
$taskConfigPath = Join-Path -Path $taskRoot -ChildPath 'task.yaml'
$baselinePath = Join-Path -Path $harnessRoot -ChildPath ("baselines\{0}.json" -f $TaskId)

if (-not (Test-Path -LiteralPath $taskRoot)) {
    throw "任务不存在：$TaskId"
}

$task = $null
$baseline = $null
$repoRoot = $null
$runId = '{0}-{1}' -f $TaskId, (Get-Date -Format 'yyyyMMdd-HHmmss')
$reportRoot = Join-Path -Path $harnessRoot -ChildPath ("reports\{0}" -f $runId)
Ensure-HarnessDirectory -Path $reportRoot
Set-Content -LiteralPath (Join-Path -Path $reportRoot -ChildPath 'stdout.log') -Value '' -Encoding utf8NoBOM
Set-Content -LiteralPath (Join-Path -Path $reportRoot -ChildPath 'stderr.log') -Value '' -Encoding utf8NoBOM

$startedAt = Get-Date
$workspaceRoot = $null
$setupStep = $null
$executionStep = $null
$verifierResults = @()
$artifacts = @()
$stepSummaries = @()
$baselineComparison = [ordered]@{
    status               = 'not_loaded'
    baseline_found       = $false
    baseline_git_sha     = $null
    expected_status      = $null
    actual_status        = $null
    verifier_differences = @()
}
$result = $null

try {
    $task = Read-TaskConfig -TaskConfigPath $taskConfigPath
    if ($task.task_id -ne $TaskId) {
        throw "task.yaml 中的 task_id 与目录不一致：$($task.task_id)"
    }

    if ([string]::IsNullOrWhiteSpace($task.base_commit)) {
        throw "task.base_commit is required"
    }

    $timeoutSec = [int]$task.timeout_sec
    if ($timeoutSec -le 0) {
        throw "task.timeout_sec must be greater than 0: $timeoutSec"
    }

    if (@($task.verifiers).Count -eq 0) {
        throw "task.verifiers must contain at least one entry"
    }

    $repoRoot = Resolve-HarnessPath -BasePath $projectRoot -RelativePath $task.repo
    if (-not (Test-Path -LiteralPath $repoRoot)) {
        throw "仓库路径不存在：$repoRoot"
    }

    if (Test-Path -LiteralPath $baselinePath) {
        $baseline = Read-HarnessJsonFile -Path $baselinePath
    }

    $workspaceRoot = New-HarnessWorkspace -RepoRoot $repoRoot -Commit $task.base_commit -RunId $runId

    $setupScript = Resolve-AndValidateTaskScript `
        -TaskRoot $taskRoot `
        -RelativePath $task.setup_script `
        -Label 'setup'

    $setupStep = Invoke-HarnessScriptStep `
        -ScriptPath $setupScript `
        -WorkspaceRoot $workspaceRoot `
        -TaskRoot $taskRoot `
        -ReportRoot $reportRoot `
        -StepName 'setup' `
        -TimeoutSec $timeoutSec

    $stepSummaries += Summarize-StepResult -StepName 'setup' -StepResult $setupStep

    if ($setupStep.exit_code -ne 0) {
        throw "setup 失败，exit_code=$($setupStep.exit_code)"
    }

    $executionScript = Resolve-AndValidateTaskScript `
        -TaskRoot $taskRoot `
        -RelativePath $task.execution_script `
        -Label 'execution'

    $executionStep = Invoke-HarnessScriptStep `
        -ScriptPath $executionScript `
        -WorkspaceRoot $workspaceRoot `
        -TaskRoot $taskRoot `
        -ReportRoot $reportRoot `
        -StepName 'execute' `
        -TimeoutSec $timeoutSec

    $stepSummaries += Summarize-StepResult -StepName 'execute' -StepResult $executionStep

    if ($executionStep.exit_code -ne 0) {
        throw "execute 失败，exit_code=$($executionStep.exit_code)"
    }

    foreach ($verifier in $task.verifiers) {
        $verifierScriptPath = Join-Path -Path $harnessRoot -ChildPath ("verifiers\{0}.ps1" -f $verifier.type)
        if (-not (Test-Path -LiteralPath $verifierScriptPath)) {
            throw "未找到 verifier：$($verifier.type)"
        }

        $verifierResults += & $verifierScriptPath `
            -WorkspaceRoot $workspaceRoot `
            -ReportRoot $reportRoot `
            -Task $task `
            -Verifier $verifier
    }

    $expectedArtifacts = @($task.expected_artifacts)
    $artifacts = foreach ($artifact in $expectedArtifacts) {
        [ordered]@{
            path   = $artifact
            exists = (Test-Path -LiteralPath (Resolve-HarnessPath -BasePath $workspaceRoot -RelativePath $artifact))
        }
    }

    $executionStatus = if (@($verifierResults | Where-Object { $_.status -eq 'failed' }).Count -gt 0) { 'failed' } else { 'passed' }
    $baselineComparison = Get-BaselineComparison -Baseline $baseline -ExecutionStatus $executionStatus -VerifierResults $verifierResults
    $overallStatus = if ($executionStatus -eq 'failed') {
        'failed'
    }
    elseif (-not $PromoteBaseline -and $baselineComparison.status -eq 'mismatched') {
        'failed'
    }
    else {
        'passed'
    }

    $result = [ordered]@{
        execution_status = $executionStatus
        overall_status   = $overallStatus
        setup_step       = $setupStep
        execution_step   = $executionStep
        verifier_results = $verifierResults
        artifacts        = @($artifacts)
        steps            = @($stepSummaries)
        baseline_comparison = $baselineComparison
        error            = $null
    }
}
catch {
    $result = [ordered]@{
        execution_status = 'failed'
        overall_status   = 'failed'
        setup_step       = $setupStep
        execution_step   = $executionStep
        verifier_results = $verifierResults
        artifacts        = @($artifacts)
        steps            = @($stepSummaries)
        baseline_comparison = $baselineComparison
        error            = $_.Exception.Message
    }
}
finally {
    $endedAt = Get-Date
    $manifestTaskId = if ($task -and $task.PSObject.Properties.Name.Contains('task_id')) { $task.task_id } else { $TaskId }
    $manifestSchemaVersion = if ($task -and $task.PSObject.Properties.Name.Contains('schema_version')) { $task.schema_version } else { $null }
    $manifestGitSha = if ($task -and $task.PSObject.Properties.Name.Contains('base_commit')) { $task.base_commit } else { $null }
    $manifestFixtureRefs = if ($task -and $task.PSObject.Properties.Name.Contains('fixtures_manifest')) { @($task.fixtures_manifest) } else { @() }
    $manifest = [ordered]@{
        run_id         = $runId
        task_id        = $manifestTaskId
        schema_version = $manifestSchemaVersion
        git_sha        = $manifestGitSha
        started_at     = $startedAt.ToString('o')
        ended_at       = $endedAt.ToString('o')
        duration_ms    = [int]($endedAt - $startedAt).TotalMilliseconds
        fixture_refs   = $manifestFixtureRefs
        baseline_ref   = if (Test-Path -LiteralPath $baselinePath) { $baselinePath } else { $null }
        environment    = Get-HarnessEnvironmentInfo
    }

    if ($workspaceRoot) {
        try {
            $diffContent = Invoke-GitText -RepoRoot $workspaceRoot -Arguments @('diff', '--relative', 'HEAD')
        }
        catch {
            $diffContent = $_.Exception.Message
        }

        Set-Content -LiteralPath (Join-Path -Path $reportRoot -ChildPath 'diff.patch') -Value ($diffContent | Out-String) -Encoding utf8NoBOM
    }

    Ensure-HarnessDirectory -Path (Join-Path -Path $reportRoot -ChildPath 'artifacts')

    if ($task -and $PromoteBaseline -and $result.execution_status -eq 'passed') {
        $baseline = [ordered]@{
            task_id          = $task.task_id
            baseline_git_sha = $task.base_commit
            expected_status  = $result.execution_status
            verifier_summary = [ordered]@{}
        }

        foreach ($verifierResult in $result.verifier_results) {
            $baseline.verifier_summary[$verifierResult.name] = $verifierResult.status
        }

        Write-HarnessJsonFile -Data $baseline -Path $baselinePath

        $result.baseline_comparison = [ordered]@{
            status               = 'promoted'
            previous_status      = $baselineComparison.status
            baseline_found       = $true
            baseline_git_sha     = $task.base_commit
            expected_status      = $result.execution_status
            actual_status        = $result.execution_status
            verifier_differences = @()
        }
        $result.overall_status = 'passed'
        $manifest.baseline_ref = $baselinePath
    }

    Write-HarnessJsonFile -Data $manifest -Path (Join-Path -Path $reportRoot -ChildPath 'manifest.json')
    Write-HarnessJsonFile -Data $result -Path (Join-Path -Path $reportRoot -ChildPath 'result.json')

    if ($workspaceRoot) {
        Remove-HarnessWorkspace -RepoRoot $repoRoot -WorkspaceRoot $workspaceRoot
    }
}

if ($result.overall_status -ne 'passed') {
    exit 1
}
