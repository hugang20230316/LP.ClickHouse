# HARNESS-002

这个任务用于验证 `.ai-harness` 的执行阶段可以直接调用 Codex，而不是回放 fixture。

- 任务背景：沿用 HARNESS-001 的真实业务改动，但把执行阶段从“复制 fixture”切换成“调用 Codex 生成补丁”。
- 业务目标：给 `ClickHouseOptions` 新增可选 `ClientName`，并在设置时写入连接字符串。
- 执行目标：在 worktree 中调用 Codex 产出 unified diff，经过范围预检后应用补丁。
- 验收目标：`build/test/diff-scope/output-check` 全通过。
- 安全约束：补丁路径必须落在 `allowed_paths`，并且不能命中 `.git/**` 与 `.ai-harness/**`。
- 留档要求：必须生成 `artifacts/agent/*` 标准证据包（prompt、raw、patch、stdout/stderr、result）。

> `task.md` 只记录面向评审的背景说明，机器执行以 `task.yaml` 为准。
