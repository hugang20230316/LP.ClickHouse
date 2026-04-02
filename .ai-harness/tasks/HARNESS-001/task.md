# HARNESS-001

这个任务用 LP.ClickHouse 里的真实代码做 harness 首轮落地验证。

- 任务背景：SDK 当前能构建 ClickHouse 连接字符串，但还不能透传客户端名称。
- 业务目标：给 `ClickHouseOptions` 新增可选 `ClientName`，并在设置时写入连接字符串。
- 为什么纳入 harness：这是一个跨 `Options`、`Builder`、单元测试三层的小改动，既能覆盖真实源码，又足够稳定，适合作为 v1 的首个仓库内任务。
- 最容易回归的点：默认值场景不应多出 `Client Name=` 段，设置值后要进连接字符串，全量测试必须通过。

不要把机器执行需要的信息写在这里。

> `task.md` 只是给 reviewer/PM 看的背景，真正被 runner 消费的字段都在同目录下的 `task.yaml`。本任务的真实代码产物都放在 `fixtures/workspace/` 下，由 `execute.ps1` 复制到工作副本。
