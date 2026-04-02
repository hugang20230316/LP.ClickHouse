# Harness v1

## 这套东西是什么

这是一套项目内最小 harness 骨架，用来把真实开发任务变成：

`固定代码起点 -> 执行 -> 验收 -> 留档 -> 对比`

## 术语

- `task`：最小可执行任务
- `runner`：负责执行任务、收集原始结果
- `verifier`：负责校验 runner 产物并判定通过/失败
- `baseline`：某个任务的固定参考结果，当前只比较整体状态和 verifier 摘要
- `report`：一次运行的结构化输出
- `fixtures`：任务输入样例

## 目录

```text
.ai-harness/
  tasks/
  verifiers/
  runner/
  baselines/
  reports/
  HARNESS.md
```

## task.yaml 规则

`task.yaml` 使用 JSON-compatible YAML，也就是直接写 JSON。

v1 必填字段：

- `task_id`
- `schema_version`
- `title`
- `goal`
- `repo`
- `base_commit`
- `setup_script`
- `execution_script`
- `allowed_paths`
- `forbidden_paths`
- `fixtures_manifest`
- `verifiers`
- `expected_artifacts`
- `timeout_sec`

### task.yaml 字段说明

- `repo`：相对于 harness 根目录的仓库路径，runner 会把它传给 `git worktree add` 并在该路径上执行。
- `base_commit`：指定要 checkout 的 Git 提交，保证每次 run 在可复现的代码版本上。
- `setup_script` / `execution_script`：对应 `_template/setup.ps1` 和 `execute.ps1`，它们会在 workspace 下运行并通过 `-WorkspaceRoot` / `-TaskRoot` / `-ReportRoot` 接收工作路径。
- `allowed_paths` / `forbidden_paths`：由 `diff-scope` verifier 读入，pattern 会统一 normalize 为 `/`，验证所有变化必须在允许范围内且不能匹配禁止路径。
- `fixtures_manifest`：指向 JSON 文件（默认在 `fixtures/manifest.json`），当前格式只需 `{"files": ["relative/path"]}`，作用是说明任务需要的样例输入。
- `verifiers`：每个对象的 `type` 决定调用 `verifiers\<type>.ps1`，`commands` / `paths` 等字段也会被这些脚本消耗。
- `expected_artifacts`：Runner 会把它列在 `result.json` 的 artifacts 信息里，`output-check` verifier 默认用这份清单做产物存在性判断。
- `timeout_sec`：Runner 会把该值传给每个命令执行，超时会触发 `Stop-Process` 并判定后续 verifier 失败。

## verifier 类型

- `build`
- `test`
- `diff-scope`
- `output-check`

`output-check` 只做硬规则校验，不做主观判断。

### verifier 与运行产物

每个 verifier 先串行执行 `commands`，遇到失败就立即返回带有 exit_code、message 的结果，Runner 会在 `result.json` 里记录每个 verifier 的 `name`/`type`/`status`/`duration_ms`，并在 `overall_status` 里聚合最终状态；目前四个 verifier 的行为如下：
 - `build` / `test`：按 `commands` 逐条调用，任何非零退出码都直接 fail；
 - `diff-scope`：分别用 `git diff --name-only` 和 `git ls-files --others` 取出 tracked/unstaged 改动，再对照 `allowed_paths`、`forbidden_paths`，命中即 fail；
 - `output-check`：确认 `Verifier.paths` 或 `Task.expected_artifacts` 内指定的文件或目录在工作区中真实存在；
 - `output-check` 和 `diff-scope` 只会读 `Task` 配置、不会执行额外命令。

## 运行

```powershell
pwsh .ai-harness/runner/run-task.ps1 -TaskId HARNESS-001
```

更新 baseline：

```powershell
pwsh .ai-harness/runner/run-task.ps1 -TaskId HARNESS-001 -PromoteBaseline
```

### 实测示例

`HARNESS-001` 是当前仓库内唯一的实际任务：`setup.ps1` 校验所需 fixture、`execute.ps1` 把五个目标文件复制到 worktree、`build/test` 验收整个 `LP.ClickHouse.slnx`、`diff-scope` 限制只改这五个文件、`output-check` 检查最终 dll。10 轮跑下来有 3 轮 pass（baseline 状态依次为 `not_found`、`promoted`、`matched`）、7 轮按预期触发 setup 缺失、空 build、范围越界、禁止路径、产物缺失、execute timeout、test 强制失败。完整执行过程、结果和每轮失败原因写在 [HARNESS_TEST_REPORT.md](/E:/Projects/LP/LP.ClickHouse/.ai-harness/HARNESS_TEST_REPORT.md) 里。

### 报告与 artifacts

每次执行后会在 `reports/<TaskId>-<yyyyMMdd-HHmmss>/` 创建结构化目录：`manifest.json`（记录 run_id、task_id、schema_version、git_sha、fixture refs、baseline 引用、环境信息、开始/结束时间、耗时）、`result.json`（execution/overall 状态、setup 与 execute 步骤结果、verifier 结果、artifacts 检查、baseline 对比结果、错误信息）、`stdout.log` / `stderr.log`（所有命令输出内容）、`diff.patch`（工作副本与 base_commit 的 diff）、`artifacts/`（空目录，便于后续采集）。`baselines/<TaskId>.json` 只在执行结果为 `passed` 且加 `-PromoteBaseline` 时更新，文件内保留整体状态与每个 verifier 的最新状态作为比较基线；普通运行在发现 baseline 文件后，会先做同一组状态比较，不一致就把本次 run 判定为失败。
