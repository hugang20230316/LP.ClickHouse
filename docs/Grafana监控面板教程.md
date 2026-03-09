# ClickHouse Grafana 监控面板教程

[返回目录](./ClickHouse教程-目录.md) | [相关章节：Part 4 - 集群部署](./ClickHouse教程-Part4-集群部署.md)

---

## 为什么需要监控？

你写了一个 ClickHouse 数据分析服务，上线后一切正常。某天早上用户反馈"查询变慢了"，你登上服务器一看——磁盘满了，Merge 积压了几百个 Parts，写入被限流。如果你有一个监控面板，这些问题在发生前几小时就能看到。

**监控不是出了问题才看的，而是用来提前发现问题的。**

---

## 面板总览

我们的 Dashboard 包含 **4 个区域、16 个面板**，按从宏观到微观的顺序排列：

| 区域 | 关注点 | 包含面板 |
|------|--------|----------|
| Server Overview / 服务器概览 | 服务是否活着、资源够不够 | Uptime、Running Queries、Memory、Disk |
| Query Performance / 查询性能 | 查询快不快、多不多 | QPS、Avg Duration、Connections |
| Insert & Merge / 写入与合并 | 数据写得进去吗、Merge 跟得上吗 | Insert Rate、Delayed Inserts、Max Parts、Merges |
| Resources / 资源使用 | 底层资源是否健康 | CPU、Rows Processed、Disk I/O、Mark Cache |

---

## 什么时候看？

### 场景一：每日巡检（2 分钟）

每天上班花 2 分钟扫一眼，重点看 **4 个指标**：

1. **Uptime** — 是不是还在跑？数值突然变小说明夜里重启过
2. **Delayed Inserts** — 是不是 0？非 0 说明写入被限流了
3. **Max Parts/Partition** — 是不是绿色？黄色就要关注了
4. **Disk Available** — 还剩多少？低于 20% 该清理或扩容了

其他面板如果没有明显异常（红色、突刺），可以跳过。

### 场景二：用户反馈"查询变慢了"

按这个顺序排查：

```
Avg Query Duration ↑ 变慢了
  → Running Queries 是不是很高？（并发太多）
  → Memory Usage 是不是接近上限？（内存不够，溢出到磁盘）
  → CPU Usage 是不是打满？（计算瓶颈）
  → Disk I/O read 是不是很高？（没命中缓存，大量读盘）
  → Mark Cache Hit Ratio 是不是很低？（缓存不足）
```

### 场景三：写入报错或变慢

```
Insert Rate ↓ 写入变慢
  → Delayed Inserts > 0？（被限流了）
  → Max Parts/Partition > 300？（Parts 太多，Merge 跟不上）
  → Merges 的 running merges 是不是很高？（Merge 在拼命追）
  → Disk I/O write 是不是很高？（磁盘写入瓶颈）
  → Disk Available 是不是快满了？（没空间写了）
```

### 场景四：容量规划（每月一次）

看趋势而不是瞬时值，把时间范围调到 **7d** 或 **30d**：

- **Disk Available** 的下降斜率 → 预估多久磁盘会满
- **Memory Usage** 的峰值趋势 → 是否需要加内存
- **QPS** 的增长趋势 → 业务量是否在增长
- **Insert Rate** 的 rows/s → 写入量是否在增长

---

## 逐面板详解

### Row 1: Server Overview / 服务器概览

这一行是"健康体检"，一眼判断服务器是否正常。

#### 1. Uptime / 运行时长

```
指标：ClickHouseAsyncMetrics_Uptime
类型：Stat（单值）
单位：秒（自动转为 x天 x小时）
```

**怎么看**：显示 ClickHouse 进程自上次启动以来的运行时间。

**正常情况**：数值持续增长，显示为绿色。

**异常信号**：
- 数值突然归零或变很小 → 服务重启了。可能原因：OOM 被系统 kill、进程崩溃、手动重启
- 排查方向：检查系统日志 `dmesg | grep -i oom`，ClickHouse 错误日志 `/var/log/clickhouse-server/clickhouse-server.err.log`

#### 2. Running Queries / 正在执行的查询

```
指标：ClickHouseMetrics_Query
类型：Stat（单值）
阈值：0-10 绿色 | 10-50 黄色 | >50 红色
```

**怎么看**：当前正在执行的查询数量（瞬时值，不是累计值）。

**正常情况**：空闲时为 0，有业务请求时在个位数波动。

**异常信号**：
- 持续 > 10 → 并发负载较高，检查是否有慢查询占着连接不释放
- 持续 > 50 → 严重拥堵，可能有查询死锁或资源耗尽
- 排查方向：`SELECT * FROM system.processes ORDER BY elapsed DESC` 查看当前正在跑的查询

#### 3. Memory Usage / 内存使用量

```
指标：ClickHouseMetrics_MemoryTracking
类型：Stat（单值）
单位：bytes（自动转为 GB/MB）
```

**怎么看**：ClickHouse 进程当前占用的内存总量，包括查询执行、缓存、Merge 等所有内存开销。

**正常情况**：随查询负载波动，空闲时较低，查询高峰时上升。

**异常信号**：
- 持续接近系统物理内存 → OOM 风险。ClickHouse 被 kill 后会自动重启，但正在执行的查询全部丢失
- 突然飙升 → 可能有大查询（如不带 WHERE 的全表扫描、大 JOIN）
- 排查方向：
  - `SELECT * FROM system.processes ORDER BY memory_usage DESC LIMIT 5` 找内存大户
  - 考虑设置 `max_memory_usage` 限制单查询内存

#### 4. Disk Available / 磁盘剩余空间

```
指标：ClickHouseAsyncMetrics_DiskAvailable_default
类型：Stat（单值）
单位：bytes（自动转为 GB/TB）
```

**怎么看**：default 磁盘策略对应的可用空间。

**正常情况**：保持在总容量的 20% 以上。

**异常信号**：
- 低于 20% → 预警，开始规划扩容或清理
- 低于 10% → 危险，Merge 可能因空间不足失败（Merge 需要临时空间）
- 接近 0 → 写入失败，服务可能不可用
- 排查方向：
  - `SELECT database, table, formatReadableSize(sum(bytes_on_disk)) FROM system.parts GROUP BY database, table ORDER BY sum(bytes_on_disk) DESC` 找最占空间的表
  - 检查 TTL 是否生效、是否有过期数据未清理

---

### Row 2: Query Performance / 查询性能

这一行关注"查询跑得怎么样"。

#### 5. QPS / 每秒查询数

```
指标：rate(SelectQuery[5m])、rate(InsertQuery[5m])、rate(FailedQuery[5m])
类型：时序图（三条线）
```

**怎么看**：三条线分别表示每秒的 SELECT、INSERT 和失败查询数量。

**正常情况**：
- select/s 和 insert/s 随业务负载波动，有明显的高峰低谷规律
- failed/s 始终为 0 或接近 0

**异常信号**：
- failed/s 突增 → 有查询在报错。常见原因：语法错误、超时、内存不足、表不存在
- select/s 突然消失 → 客户端连不上了，检查网络和连接数
- insert/s 突然下降 → 写入被限流，看 Delayed Inserts
- 排查方向：`SELECT type, last_error_message, count() FROM system.errors GROUP BY type, last_error_message ORDER BY count() DESC`

#### 6. Avg Query Duration / 平均查询耗时

```
指标：rate(SelectQueryTimeMicroseconds[5m]) / rate(SelectQuery[5m])
类型：时序图（两条线：avg select、avg insert）
单位：微秒（µs）
```

**怎么看**：SELECT 和 INSERT 的平均执行时间。注意是 5 分钟滑动窗口的平均值，不是单次查询耗时。

**正常情况**：
- 空闲时无数据（除数为 0）是正常的，不是故障
- SELECT 通常在毫秒到秒级，取决于数据量和查询复杂度
- INSERT 通常很快（微秒级），因为 ClickHouse 写入是追加式的

**异常信号**：
- avg select 突然翻倍 → 查询变慢了。排查顺序：
  1. 是否有新的慢查询上线？
  2. Mark Cache Hit Ratio 是否下降？
  3. 磁盘 I/O 是否饱和？
  4. 内存是否不足导致溢出到磁盘？
- avg insert 变慢 → 通常是 Merge 积压或磁盘 I/O 瓶颈

#### 7. Connections / 连接数

```
指标：ClickHouseMetrics_HTTPConnection、ClickHouseMetrics_TCPConnection
类型：时序图（两条线）
```

**怎么看**：当前活跃的客户端连接数，按协议分为 HTTP 和 TCP。

**协议区别**：
- **HTTP**：.NET ClickHouse.Client 驱动、HTTP API 调用、Grafana 数据源等
- **TCP**：clickhouse-client 命令行、部分原生驱动、Prometheus 抓取（会产生 1 个 TCP 连接）

**正常情况**：随业务负载波动，空闲时 HTTP 可能为 0，TCP 至少有 1（Prometheus 抓取）。

**异常信号**：
- 连接数持续增长不释放 → 连接泄漏，客户端没有正确关闭连接
- 突然归零 → 网络问题或 ClickHouse 不可用
- 排查方向：`SELECT * FROM system.processes` 查看每个连接在做什么

---

### Row 3: Insert & Merge / 写入与合并

这一行是 ClickHouse 特有的核心监控区域。理解 Merge 机制是用好 ClickHouse 的关键。

**背景知识**：ClickHouse 每次 INSERT 都会创建一个新的数据块（Part）。后台线程会自动将小 Parts 合并为大 Parts（Merge）。如果写入太快、Merge 跟不上，Parts 数量会堆积，最终触发限流甚至拒绝写入。

```
写入流程：
  INSERT → 创建新 Part → 后台 Merge → 合并为大 Part

  如果 Merge 跟不上：
  Parts 堆积 → 300: 写入减速 → 1000: 拒绝写入（TOO_MANY_PARTS）
```

#### 8. Insert Rate / 写入速率

```
指标：rate(InsertedRows[5m])、rate(InsertedBytes[5m])
类型：时序图（两条线）
```

**怎么看**：每秒写入的行数和字节数。两条线的趋势应该一致。

**正常情况**：与业务写入节奏一致，批量导入时会有尖峰。

**异常信号**：
- 突然下降到 0 → 写入停了。检查客户端是否正常、是否被限流
- rows/s 很高但 bytes/s 很低 → 每行数据很小，可能是小批量高频写入（不推荐）
- 建议：每次 INSERT 至少攒 1000-10000 行，避免每行一个 INSERT

#### 9. Delayed Inserts / 延迟写入

```
指标：ClickHouseMetrics_DelayedInserts
类型：Stat（单值，带背景色）
阈值：0 绿色 | ≥1 红色
```

**怎么看**：这是一个"红绿灯"面板。绿色 = 正常，红色 = 出问题了。

**正常情况**：始终为 0（绿色背景）。

**异常信号**：
- > 0（红色背景）→ ClickHouse 正在主动限流写入。原因是 Parts 数量接近阈值，系统通过减慢写入速度来给 Merge 争取时间
- 这是一个**预警信号**，说明 Max Parts/Partition 即将进入黄色区域
- 处理方式：
  1. 减少写入频率，增大每次写入的批量
  2. 检查是否有太多小表或分区
  3. 等待 Merge 追上来（观察 Merges 面板）

#### 10. Max Parts/Partition / 单分区最大 Parts 数

```
指标：ClickHouseAsyncMetrics_MaxPartCountForPartition
类型：时序图（带阈值线）
阈值：<300 绿色 | 300-1000 黄色 | >1000 红色
```

**怎么看**：所有表的所有分区中，Parts 数量最多的那个分区的 Parts 数。这是 ClickHouse 最重要的健康指标之一。

**类比**：把 Parts 想象成桌上的文件。每次写入放一份新文件，Merge 就是整理员把小文件合并成大文件。如果文件堆到 300 份，整理员会喊"别再放了，让我先整理"；堆到 1000 份，直接拒收。

**正常情况**：通常在几十以内。

**阈值含义**：
- **< 300（绿色）**：健康，Merge 跟得上写入
- **300-1000（黄色）**：写入开始被减速（Delayed Inserts > 0），Merge 在追赶
- **> 1000（红色）**：触发 `TOO_MANY_PARTS` 错误，写入被拒绝

**排查方向**：
```sql
-- 找出哪个表哪个分区 Parts 最多
SELECT database, table, partition, count() as parts
FROM system.parts
WHERE active
GROUP BY database, table, partition
ORDER BY parts DESC
LIMIT 10
```

#### 11. Merges / 后台合并

```
指标：ClickHouseMetrics_Merge、rate(MergedRows[5m])
类型：时序图（两条线）
```

**怎么看**：
- **running merges**：当前正在执行的 Merge 任务数
- **merged rows/s**：每秒合并的行数，反映 Merge 的吞吐量

**正常情况**：有写入时 running merges 通常 > 0，说明后台在正常工作。

**异常信号**：
- running merges 持续很高 + Max Parts 在增长 → Merge 跟不上写入，需要减少写入频率或优化表结构
- running merges = 0 但 Max Parts 很高 → Merge 可能卡住了，检查磁盘空间和错误日志
- merged rows/s 突然归零 → Merge 停了，可能是磁盘满或系统资源不足

---

### Row 4: Resources / 资源使用

这一行监控底层硬件资源，帮助定位性能瓶颈的根因。

#### 12. CPU Usage / CPU 使用率

```
指标：sum(OSUserTimeCPU*)、sum(OSSystemTimeCPU*)
类型：时序图（堆叠面积图）
单位：CPU 核心数
```

**怎么看**：ClickHouse 进程占用的 CPU 核心数，按用户态和内核态堆叠显示。

**两种 CPU 时间**：
- **user（用户态）**：查询计算、数据压缩/解压、排序等。这是 ClickHouse 自己的工作
- **system（内核态）**：磁盘 I/O、网络收发等。这是操作系统帮 ClickHouse 做的工作

**怎么读数值**：值 = 占用的 CPU 核心数。例如值为 2.5 表示占用了 2.5 个核心。如果你的机器有 8 核，2.5 就是 31% 的总 CPU。

**正常情况**：随查询负载波动，空闲时接近 0。

**异常信号**：
- user 持续接近总核心数 → CPU 瓶颈，查询在排队等 CPU
- system 占比很高 → I/O 密集，可能是大量读盘或网络传输
- 排查方向：找出 CPU 密集的查询 `SELECT query, read_rows, elapsed FROM system.processes ORDER BY elapsed DESC`

#### 13. Rows Processed / 行处理速率

```
指标：rate(SelectedRows[5m])、rate(InsertedRows[5m])
类型：时序图（两条线）
```

**怎么看**：每秒处理的数据行数，分为查询读取和写入两个方向。

**正常情况**：与业务负载一致。

**实用价值**：
- selected rows/s 远大于 inserted rows/s → 读多写少的分析型负载（典型 OLAP）
- 两者接近 → 读写均衡
- selected rows/s 突然飙升 → 可能有全表扫描查询，检查是否缺少 WHERE 条件或排序键未命中

#### 14. Disk I/O / 磁盘读写

```
指标：rate(OSReadBytes[5m])、rate(OSWriteBytes[5m])
类型：时序图（两条线）
单位：Bytes/s（自动转为 MB/s、GB/s）
```

**怎么看**：ClickHouse 进程的磁盘读写速率。

**读写对应的操作**：
- **read**：查询读取数据文件、Mark 文件
- **write**：INSERT 写入新 Part、Merge 写入合并后的 Part

**正常情况**：随负载波动。SSD 通常能支撑 500MB/s+ 的读写。

**异常信号**：
- read 持续很高 → 查询在大量读盘，可能是 Mark Cache 命中率低或查询扫描范围太大
- write 持续很高 → 大量写入或 Merge 活动，检查磁盘是否能承受
- 读写都很高 → 磁盘可能成为瓶颈，查询和 Merge 在争抢 I/O

#### 15. Mark Cache Hit Ratio / Mark 缓存命中率

```
指标：rate(MarkCacheHits[5m]) / (rate(MarkCacheHits[5m]) + rate(MarkCacheMisses[5m]))
类型：时序图
单位：百分比（0-100%）
```

**怎么看**：Mark 文件缓存的命中率。

**什么是 Mark 文件**：ClickHouse 的数据按 Granule（默认 8192 行）存储。Mark 文件记录了每个 Granule 在数据文件中的偏移位置。查询时先读 Mark 文件定位数据位置，再读取实际数据。Mark 文件很小但访问极其频繁。

**类比**：Mark 文件就像书的目录页。每次查内容都要先翻目录。如果目录页能记在脑子里（缓存命中），直接翻到对应页码；如果记不住（缓存未命中），每次都要从书架上拿出目录页来看。

**正常情况**：应该接近 100%（> 95%）。空闲时无数据是正常的。

**异常信号**：
- 低于 90% → 缓存不足，大量查询需要从磁盘读取 Mark 文件，会显著增加查询延迟
- 持续下降 → 表或分区数量增长，Mark 文件总量超过了缓存容量
- 解决方案：在 ClickHouse 配置中增大 `mark_cache_size`（默认 5GB）

---

## 快速参考：阈值速查表

| 指标 | 正常 | 警告 | 危险 |
|------|------|------|------|
| Uptime | 持续增长 | — | 突然归零 |
| Running Queries | 0-10 | 10-50 | > 50 |
| Memory Usage | < 70% 系统内存 | 70-90% | > 90% |
| Disk Available | > 20% 总容量 | 10-20% | < 10% |
| Failed QPS | 0 | 偶发 | 持续 > 0 |
| Delayed Inserts | 0 | — | ≥ 1 |
| Max Parts/Partition | < 300 | 300-1000 | > 1000 |
| Mark Cache Hit Ratio | > 95% | 90-95% | < 90% |

---

## 常见问题排查流程

### 流程一：查询变慢

```
1. 看 Avg Query Duration → 确认确实变慢了
2. 看 Running Queries → 并发是否过高
3. 看 Memory Usage → 是否接近上限
4. 看 CPU Usage → 是否打满
5. 看 Disk I/O read → 是否大量读盘
6. 看 Mark Cache Hit Ratio → 缓存是否失效
7. 查 system.query_log 找到具体慢查询 → 优化 SQL 或加索引
```

### 流程二：写入失败

```
1. 看 Delayed Inserts → 是否被限流
2. 看 Max Parts/Partition → 是否超过 300
3. 看 Merges → Merge 是否在工作
4. 看 Disk Available → 磁盘是否满了
5. 看 Insert Rate → 写入量是否异常大
6. 减少写入频率 / 增大批量 / 等待 Merge 追上
```

### 流程三：服务重启

```
1. 看 Uptime → 确认重启时间点
2. 看重启前的 Memory Usage → 是否 OOM
3. 检查系统日志 dmesg → 是否被 OOM Killer 杀掉
4. 检查 ClickHouse 错误日志 → 是否有崩溃堆栈
5. 看重启前的 Max Parts → 是否 Parts 爆炸导致异常
```

---

## Grafana 操作技巧

### 调整时间范围

- 右上角时间选择器：`Last 6 hours`（默认）、`Last 24 hours`、`Last 7 days`
- 日常巡检用 6h，排查问题用 1h-3h（看细节），容量规划用 7d-30d（看趋势）

### 查看具体数值

- 鼠标悬停在图表上 → 显示该时间点的精确数值
- 图表支持 Tooltip 联动（`graphTooltip: 1`），悬停一个图表时其他图表也会显示同一时间点的值

### 面板全屏

- 点击面板标题 → View → 全屏查看，适合排查时仔细观察某个指标

---

> 💡 **记住**：监控面板的价值不在于面板本身，而在于你对每个指标含义的理解。当你能看一眼面板就知道"系统现在怎么样"，监控就发挥了它的作用。
