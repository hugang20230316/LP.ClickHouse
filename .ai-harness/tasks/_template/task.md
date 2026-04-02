# TASK-001

这里写给 reviewer/PM 的背景说明，重点表述：

- 任务背景
- 业务目标
- 为什么适合纳入 harness
- 这个任务最容易回归的点

不要把机器执行需要的信息写在这里。

> `task.md` 只是给人看的上下文，真正被 runner 消费的字段都在同目录下的 `task.yaml`。写完人类版背景后，按 `task.yaml` 模板补充 `setup_script`/`execution_script`/`fixtures_manifest`/`verifiers` 等机器字段；`HARNESS_TEST_REPORT.md` 中已经记录了 10 轮 HARNESS-001 的执行结果，按该格式来设计新任务时可以参考“场景 → 预期 → 实际”组合，并把模拟场景写入 `task.md` 以供复盘。
