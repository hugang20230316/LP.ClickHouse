# LP.ClickHouse Harness 测试报告

> 本报告由仓库内 harness 实测生成，目标是让阅读者在不看实现细节的情况下，也能理解这套 harness 的作用、使用方式、执行过程和设计原理。

## 1. Harness 在这个仓库里是做什么的

这套 harness 用来把一个真实代码任务收敛成稳定可复跑的流程：

`固定代码起点 -> 执行任务 -> 构建/测试/范围校验 -> 结构化留档 -> 可选 baseline 对比`

在 `LP.ClickHouse` 仓库里，首个真实任务是：

- `HARNESS-001`
- 目标：给 `ClickHouseOptions` 增加可选 `ClientName`，并让 `ClickHouseConnectionStringBuilder` 在设置值时透传 `Client Name=...;`
- 验收：全量 `dotnet build` 和 `dotnet test` 通过，改动范围严格限定在 5 个目标文件内

## 2. 这套 harness 什么时候使用

适合这几类任务：

- 改动范围小到中等，能被明确限制到一组文件
- 能用构建、测试、产物存在性和 diff 范围做确定性验收
- 需要把“AI 或脚本执行某个任务”反复复跑，并希望结果标准一致
- 想给后续 agent/人工评审提供结构化报告，而不是只看零散终端日志

不适合的场景：

- 依赖主观审美判断的任务
- 必须连公司环境或生产数据的任务
- 需要远程部署、人工点击、跨系统联调的任务

## 3. 这个仓库里怎么使用

### 3.1 直接运行任务

在仓库根目录执行：

```powershell
pwsh .ai-harness/runner/run-task.ps1 -TaskId HARNESS-001
```

### 3.2 生成或更新 baseline

```powershell
pwsh .ai-harness/runner/run-task.ps1 -TaskId HARNESS-001 -PromoteBaseline
```

说明：

- 普通运行：如果没有 baseline，会直接按本次执行结果判定
- 有 baseline 时：runner 会自动把本次 `overall_status` 和每个 verifier 状态与 baseline 对比
- `-PromoteBaseline`：只在本次执行成功时更新 `.ai-harness/baselines/HARNESS-001.json`

### 3.3 任务文件分别负责什么

- `.ai-harness/tasks/HARNESS-001/task.md`
  作用：写给人看的背景和回归点
- `.ai-harness/tasks/HARNESS-001/task.yaml`
  作用：机器真源，定义 base commit、脚本、verifier、允许改动范围、超时等
- `.ai-harness/tasks/HARNESS-001/setup.ps1`
  作用：执行前准备，本任务用于校验 fixture 是否齐全
- `.ai-harness/tasks/HARNESS-001/execute.ps1`
  作用：把 `fixtures/workspace/` 里的目标文件复制进 worktree，形成真实代码改动
- `.ai-harness/tasks/HARNESS-001/fixtures/manifest.json`
  作用：列出本任务依赖的 fixture 文件

## 4. 执行过程是怎样的

以 `HARNESS-001` 为例，runner 的过程如下：

1. 读取 `task.yaml`
2. 基于 `base_commit` 创建临时 `git worktree`
3. 执行 `setup.ps1`
4. 执行 `execute.ps1`
5. 依次运行四类 verifier：
   - `build`
   - `test`
   - `diff-scope`
   - `output-check`
6. 生成结构化报告：
   - `manifest.json`
   - `result.json`
   - `stdout.log`
   - `stderr.log`
   - `diff.patch`
7. 如有 baseline，则做状态对比
8. 清理临时 worktree

## 5. 设计原理为什么这样

### 5.1 固定代码起点

每次都从 `base_commit` 拉出临时 worktree，避免“上一次运行遗留改动”污染下一次结果。

### 5.2 任务与主仓库解耦

真实改动内容不直接写进仓库源码，而是放在 `fixtures/workspace/`，由 `execute.ps1` 注入 worktree。  
这样既能测试真实代码任务，也不会把试验改动直接落进仓库业务代码。

### 5.3 验收必须确定性

这里只允许确定性 verifier：

- `build`：项目能不能构建
- `test`：测试能不能通过
- `diff-scope`：是否只改了允许的文件
- `output-check`：预期产物是否存在

### 5.4 baseline 只做轻量比对

当前 baseline 只比较：

- 本次 `overall_status`
- 每个 verifier 的 `status`

这样成本低、结果稳定，适合 v1。

## 6. 10 轮实测结果

执行 `HARNESS-001` 10 轮，结果如下：

详细机器可读索引见 `.ai-harness/HARNESS_TEST_RESULTS.json`。

| 轮次 | 场景 | 预期 | 结果 | 备注 |
| 1 | 首次运行、无 baseline | 退出码 0，`overall_status=passed`，`baseline_status=not_found` | ✅pass | 记录 baseline 前的成功快照 |
| 2 | 尝试 `-PromoteBaseline` | 同上但 `baseline_status=promoted` | ✅pass | baseline 文件生成 |
| 3 | 复跑、期望 baseline 匹配 | `baseline_status=matched` | ✅pass | 证明重复运行不会触发 diff |
| 4 | Setup 脚本路径缺失 | `exit=1`，`setup script not found` | ❌fail | 知识点：`Resolve-AndValidateTaskScript` 会提早失速 |
| 5 | `build` verifier 命令数组为空 | `exit=1`，`verifier 'build' 必须包含至少一个命令` | ❌fail | 防止空命令掩盖构建失败 |
| 6 | 把 `allowed_paths` 限制到单个文件 | `diff-scope` 报 `改动超出允许范围` | ❌fail | 模拟范围收敛导致的 reject |
| 7 | 在 `forbidden_paths` 增加测试文件 | `diff-scope` 报 `命中禁止改动路径` | ❌fail | 验证 diff scope 拦截配置文件 |
| 8 | `output-check` 指向不存在产物 | `缺少预期产物` | ❌fail | 验证产物检查的输出 |
| 9 | `timeout_sec` 设置为 1 秒 · `execute.ps1` 睡 3 秒 | `execute 失败，exit_code=124` | ❌fail | 验证 runner 终止超时 |
| 10 | `test` verifier 强制退出 1 | `baseline_status=mismatched` | ❌fail | Baseline 记录旧状态，检测到差异 |

## 7. 过程中发现的问题

- Windows 下 `git` 会把 `LF -> CRLF` 报 warning，`diff-scope` 需要过滤这类提示，否则会误判 “改动超出范围”，已在 verifier 中加入过滤。
- `HARNESS-001` 依赖 `fixtures/workspace` 里的源码拷贝，`setup.ps1` 会验证这些文件存在，防止缺失文件导致后续命令操作空文件。
- Baseline 对比只聚焦 `overall_status` 与 verifier 状态，后续可扩展到硬编码产物哈希，如果需要更强的防回归保障。

## 8. 三个子代理复审结论

- 文档代理：已经把 HARNESS.md 补充为真实运行流程，并将 10 轮结果写入报告；`HARNESS_TEST_REPORT.md` 经此轮跑完后也列出了表格与问题清单。
- 任务代理：确认 `task.md`/`task.yaml` 保持最小且指向 fixture，`execute.ps1` 仅复制指定文件，不写业务代码，`setup.ps1` 校验 fixture 文件后再继续。
- runner/verifier 代理：`diff-scope` 过滤 `git` 警告，runner 继续生成 struct 报告，baseline 逻辑已经按传入需求进行了轻量对比。

## 9. 子代理修正后的最终复测

3 个子代理修正合并后，又额外执行了 1 轮稳定复测：

- 命令：`pwsh .ai-harness/runner/run-task.ps1 -TaskId HARNESS-001`
- 结果：`overall_status=passed`
- baseline 状态：`matched`
- 详细索引：见 `.ai-harness/HARNESS_TEST_RESULTS.json` 中的 `post_review_validation`
