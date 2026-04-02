param(
    [Parameter(Mandatory)][string]$WorkspaceRoot,
    [Parameter(Mandatory)][string]$ReportRoot,
    [Parameter(Mandatory)]$Task,
    [Parameter(Mandatory)]$Verifier
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path -Path (Split-Path -Path $PSScriptRoot -Parent) -ChildPath 'runner\Harness.Common.ps1')

$commands = @($Verifier.commands)
if (@($commands).Count -eq 0) {
    throw "verifier '$($Verifier.name)' 必须包含至少一个命令。"
}

$total = 0
foreach ($command in @($commands)) {
    $result = Invoke-HarnessCommand -Command $command -WorkingDirectory $WorkspaceRoot -ReportRoot $ReportRoot -StepName $Verifier.name -TimeoutSec ([int]$Task.timeout_sec)
    $total += $result.duration_ms

    if ($result.exit_code -ne 0) {
        return [ordered]@{
            name        = $Verifier.name
            type        = $Verifier.type
            status      = 'failed'
            exit_code   = $result.exit_code
            duration_ms = $total
            message     = "命令失败：$command"
        }
    }
}

return [ordered]@{
    name        = $Verifier.name
    type        = $Verifier.type
    status      = 'passed'
    exit_code   = 0
    duration_ms = $total
    message     = '测试通过'
}
