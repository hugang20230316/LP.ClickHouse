# HARNESS-003

这个任务用于验证 `.ai-harness` 可以驱动 Codex 为示例 API 增加 Swagger，而不是只处理 Core 层的小范围字段透传。

- 任务背景：示例 API 目前只暴露控制器路由，没有可直接浏览的接口文档页面。
- 业务目标：让 `samples/LP.ClickHouse.Api` 支持 Swagger JSON 与 Swagger UI，便于本地调试和演示。
- 执行目标：在 worktree 中调用 Codex，只修改示例 API 的 `Program.cs` 和 `.csproj`。
- 验收目标：`build/test/diff-scope/output-check` 全通过。
- 安全约束：补丁路径必须落在 `allowed_paths`，并且不能命中 `.git/**` 与 `.ai-harness/**`。
- 留档要求：必须生成 `artifacts/agent/*` 标准证据包（prompt、raw、patch、stdout/stderr、result）。
