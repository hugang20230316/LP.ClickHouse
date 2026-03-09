# ClickHouse 教程 - Part 4: 集群部署

[返回目录](./ClickHouse教程-目录.md) | [上一章：Part 3 - 性能优化](./ClickHouse教程-Part3-性能优化.md)

---

## 12. 分布式表与集群架构

### 业务场景

你的电商平台每天产生 5000 万条用户行为日志，单机 ClickHouse 每天最多处理 1000 万行写入，磁盘也只剩 2 TB。更要命的是，老板说"这个系统不能挂"——单机一旦宕机，整个数据分析平台就瘫痪了。

**你需要同时解决两个问题：数据量超过单机容量，以及单点故障风险。**

### 先搞清楚两个概念：分片 vs 副本

很多人第一次接触 ClickHouse 集群时会困惑：分片（Shard）和副本（Replica）到底是什么关系？

**类比**：把数据想象成一本 1000 页的书。
- **分片**：把书撕成 3 份，每人拿 333 页——这是水平拆分，解决"一个人看不完"的问题
- **副本**：把每份复印一份——这是冗余备份，解决"万一丢了"的问题

**原理**：
- **分片（Shard）**= 数据的水平拆分。每个分片存储不同的数据子集，查询时并行扫描所有分片再合并结果。目的是**扩容**
- **副本（Replica）**= 数据的冗余拷贝。同一个分片内的多个副本存储完全相同的数据，某个副本挂了，另一个顶上。目的是**高可用**

这两个维度是正交的，可以自由组合：

```
┌─────────────────────────────────────────────────────────┐
│                    Distributed 表                        │
│              （逻辑层，不存数据，只做路由）                  │
└──────────┬──────────────────────┬────────────────────────┘
           │                      │
     ┌─────▼─────┐          ┌────▼──────┐
     │  Shard 1  │          │  Shard 2  │
     │ (数据 A)  │          │ (数据 B)  │
     └─────┬─────┘          └─────┬─────┘
       ┌───┴───┐              ┌───┴───┐
       │       │              │       │
   ┌───▼──┐ ┌─▼────┐    ┌───▼──┐ ┌─▼────┐
   │node1 │ │node2  │    │node3 │ │node4  │
   │副本1 │ │副本2  │    │副本1 │ │副本2  │
   └──────┘ └──────┘    └──────┘ └──────┘
   （所有副本都可读写，不是主从关系）
```

上图是最常见的 **2 分片 × 2 副本** 架构：
- 数据 A 和数据 B 分别存在不同分片（水平拆分）
- 每个分片有 2 个副本（冗余备份）
- 任何一个节点挂掉，集群仍然可用

### Distributed 表：集群的"路由层"

Distributed 表本身不存储任何数据，它是一个路由代理：
- **写入时**：根据分片键把数据分发到对应分片的本地表
- **查询时**：向所有分片发起并行查询，收集结果后合并返回

```
写入流程：
Client → Distributed 表 → 根据 sharding_key 计算目标分片 → 写入对应分片的本地表

查询流程：
Client → Distributed 表 → 并行发送子查询到所有分片 → 收集结果 → 合并 → 返回
```

### 实现

**步骤 1：配置集群（2 分片 × 2 副本）**

编辑 `/etc/clickhouse-server/config.d/cluster.xml`：

```xml
<clickhouse>
    <remote_servers>
        <my_cluster>
            <!-- 分片 1：node1 和 node2 互为副本 -->
            <shard>
                <internal_replication>true</internal_replication>
                <replica>
                    <host>node1</host>
                    <port>9000</port>
                </replica>
                <replica>
                    <host>node2</host>
                    <port>9000</port>
                </replica>
            </shard>
            <!-- 分片 2：node3 和 node4 互为副本 -->
            <shard>
                <internal_replication>true</internal_replication>
                <replica>
                    <host>node3</host>
                    <port>9000</port>
                </replica>
                <replica>
                    <host>node4</host>
                    <port>9000</port>
                </replica>
            </shard>
        </my_cluster>
    </remote_servers>
</clickhouse>
```

`internal_replication` 是一个关键参数：
- `true`（推荐）：Distributed 表只写入一个副本，由 ReplicatedMergeTree 自己同步到其他副本
- `false`：Distributed 表向所有副本都写一份——看起来简单，但容易造成数据不一致

**步骤 2：每个节点配置 macros**

每个节点的 macros 不同，用于 ReplicatedMergeTree 自动识别自己的身份：

```xml
<!-- node1 的 macros -->
<macros>
    <shard>01</shard>
    <replica>node1</replica>
</macros>

<!-- node2 的 macros -->
<macros>
    <shard>01</shard>
    <replica>node2</replica>
</macros>

<!-- node3 的 macros -->
<macros>
    <shard>02</shard>
    <replica>node3</replica>
</macros>

<!-- node4 的 macros -->
<macros>
    <shard>02</shard>
    <replica>node4</replica>
</macros>
```

**步骤 3：创建本地表（使用 ON CLUSTER 一次搞定）**

```sql
-- ON CLUSTER 会自动在集群所有节点上执行这条 DDL
CREATE TABLE api_logs_local ON CLUSTER my_cluster (
    timestamp DateTime,
    user_id UInt64,
    api_path String,
    status_code UInt16,
    response_time_ms UInt32
) ENGINE = ReplicatedMergeTree('/clickhouse/tables/{shard}/api_logs', '{replica}')
PARTITION BY toYYYYMMDD(timestamp)
ORDER BY (api_path, timestamp);
-- {shard} 和 {replica} 会自动替换为每个节点的 macros 值
```

**步骤 4：创建分布式表**

```sql
CREATE TABLE api_logs_distributed ON CLUSTER my_cluster
AS api_logs_local
ENGINE = Distributed(my_cluster, default, api_logs_local, cityHash64(user_id));
-- cityHash64(user_id)：同一个用户的数据总是落在同一个分片
-- 这样按 user_id 查询时可以只扫描一个分片，而不是所有分片
```

**步骤 5：写入和查询**

```sql
-- 写入分布式表，数据自动按 user_id 哈希分散到 2 个分片
INSERT INTO api_logs_distributed VALUES
    (now(), 1001, '/api/orders', 200, 45),
    (now(), 2002, '/api/users', 200, 12),
    (now(), 1001, '/api/orders/123', 404, 8);

-- 查询分布式表，自动并行查询 2 个分片再合并
SELECT
    api_path,
    count() AS request_count,
    avg(response_time_ms) AS avg_response_ms
FROM api_logs_distributed
WHERE timestamp >= now() - INTERVAL 1 HOUR
GROUP BY api_path
ORDER BY request_count DESC;
```

### 分片键的选择策略

分片键决定数据如何分布到各个分片，选错了会导致数据倾斜或查询效率低下：

| 分片键 | 适用场景 | 优点 | 缺点 |
|--------|---------|------|------|
| `rand()` | 日志类数据，无特定查询模式 | 数据分布最均匀 | 按特定维度查询时必须扫描所有分片 |
| `cityHash64(user_id)` | 经常按用户维度查询 | 同一用户数据在同一分片，查询可剪枝 | 大用户可能导致数据倾斜 |
| `cityHash64(tenant_id)` | 多租户 SaaS 系统 | 租户数据隔离，查询效率高 | 大租户问题同上 |
| `toYYYYMM(timestamp)` | 时序数据，按时间范围查询 | 时间范围查询只扫描相关分片 | 最新月份的分片压力最大 |

```sql
-- ❌ 错误：用低基数字段做分片键
ENGINE = Distributed(my_cluster, default, api_logs_local, status_code);
-- status_code 只有几十个值，数据会严重倾斜（200 占 90%）

-- ✅ 正确：用高基数字段的哈希值
ENGINE = Distributed(my_cluster, default, api_logs_local, cityHash64(user_id));
-- user_id 基数高，哈希后分布均匀
```

### 关键点

1. **Distributed 表不存数据**：它只是路由层，真正的数据在各节点的本地表（ReplicatedMergeTree）里
2. **ON CLUSTER 简化 DDL**：不需要逐个节点执行建表语句
3. **internal_replication 必须设为 true**：配合 ReplicatedMergeTree 使用时，让引擎自己处理副本同步
4. **分片键选择影响查询性能**：如果 90% 的查询都带 `WHERE user_id = ?`，那就用 `cityHash64(user_id)` 做分片键

### 验证

```sql
-- 检查数据分布是否均匀
SELECT
    hostName() AS node,
    count() AS rows
FROM api_logs_distributed
GROUP BY node;
-- 理想情况：每个分片的行数大致相等

-- 检查分布式查询是否真的并行了
SELECT
    query,
    query_duration_ms,
    read_rows,
    ProfileEvents['DistributedConnectionTries'] AS distributed_tries
FROM system.query_log
WHERE type = 'QueryFinish'
  AND query LIKE '%api_logs_distributed%'
ORDER BY event_time DESC
LIMIT 5;
```

---

## 13. 数据同步与高可用

### 业务场景

凌晨 3 点，node2 的磁盘突然坏了。运维收到告警，但不敢重启——上面跑着每天 5000 万行的日志数据。

**问题**：
- node2 挂了，它上面的数据还在吗？
- 用户的查询会受影响吗？
- node2 修好后，数据怎么补回来？

如果你用的是普通 MergeTree，答案是：数据丢了，查询报错，手动恢复。
如果你用的是 ReplicatedMergeTree，答案是：**数据没丢，查询自动切到 node1，node2 修好后自动追数据**。

### 副本同步机制：ZooKeeper 的角色

很多人以为 ZooKeeper 负责同步数据。**错了。** ZooKeeper 只存储操作日志（几 KB），不存储实际数据（可能几 TB）。

**类比**：ZooKeeper 像一个共享的"待办事项清单"。
- node1 写入了一批数据 → 在清单上记一笔："我插入了 partition 20260305 的 data part all_0_0_0"
- node2 定期检查清单 → 发现有新任务 → 从 node1 下载这个 data part → 标记任务完成

**完整的写入-同步流程**：

```
1. Client 写入 node1（副本 1）
   │
2. node1 将数据写入本地磁盘，生成 data part
   │
3. node1 在 ZooKeeper 中记录一条日志：
   │  /clickhouse/tables/01/api_logs/log/log-0000000001
   │  内容：{"type":"get","source":"node1","part_name":"all_0_0_0",...}
   │  （data part = ClickHouse 写入磁盘的最小数据单元，一次 INSERT 生成一个 part）
   │
4. node2 监听到 ZooKeeper 日志变化
   │
5. node2 从 node1 下载 data part（通过 HTTP，端口 9009）
   │
6. node2 写入本地磁盘，在 ZooKeeper 中确认完成
```

**关键细节**：
- 数据传输走的是节点之间的直连（HTTP 9009 端口），不经过 ZooKeeper
- ZooKeeper 只存元数据和操作日志，体积很小
- 同步是异步的——写入 node1 成功后立即返回客户端，不等 node2 同步完成

### Leader 与 Follower

每个分片内的副本会通过 ZooKeeper 选举一个 Leader：

| 角色 | 职责 |
|------|------|
| Leader | 协调 MERGE 操作（后台合并 data parts）、处理 MUTATION（数据变更，即 ALTER UPDATE/DELETE） |
| Follower | 执行 Leader 分配的 MERGE 任务，接受写入和查询 |

**注意**：Leader 和 Follower 的区别只在后台操作的协调上。**所有副本都可以接受写入和查询**，这不是 MySQL 那种主从架构。

```sql
-- 查看谁是 Leader
SELECT
    database,
    table,
    replica_name,
    is_leader,
    is_readonly
FROM system.replicas
WHERE table = 'api_logs_local';
```

### 故障场景与自动恢复

#### 场景 1：一个副本宕机

```
正常状态：
  Shard 1: [node1 ✅] [node2 ✅]

node2 宕机：
  Shard 1: [node1 ✅] [node2 ❌]

影响：
  - 读取：自动路由到 node1，用户无感知
  - 写入：继续写入 node1，ZooKeeper 记录日志
  - node2 恢复后：自动从 ZooKeeper 日志中找到缺失的 data parts，
    从 node1 下载补齐，追上进度后恢复正常服务
```

#### 场景 2：ZooKeeper 宕机

```
ZooKeeper 不可用：
  - 读取：正常（不依赖 ZK）
  - 写入：失败（无法记录同步日志）
  - DDL：失败（ON CLUSTER 依赖 ZK）
  - 副本同步：暂停

ZooKeeper 恢复后：
  - 积压的写入日志开始同步
  - 副本自动追赶进度
```

这就是为什么 ZooKeeper 本身也要部署 3 节点集群（至少 3 个，允许挂 1 个）。

#### 场景 3：数据不一致（脑裂恢复后）

极端情况下，两个副本的数据可能不一致。ClickHouse 提供了修复命令：

```sql
-- 检查副本数据一致性
CHECK TABLE api_logs_local;

-- 如果不一致，从其他副本修复
SYSTEM RESTART REPLICA api_logs_local;

-- 强制从 ZooKeeper 元数据重建
SYSTEM RESTORE REPLICA api_logs_local;
```

### Quorum 写入：当你需要强一致性

默认情况下，写入一个副本就返回成功，同步是异步的。如果 node1 写入成功后立刻宕机，而 node2 还没来得及同步，这批数据就暂时"只有一份"。

对于日志数据，这通常可以接受。但对于订单、支付等关键数据，你可能需要 **Quorum 写入**：

```sql
-- 设置 Quorum：写入必须在 N 个副本上确认后才返回成功
SET insert_quorum = 2;
-- 2 个副本都写入成功才返回，保证数据至少有 2 份

INSERT INTO api_logs_local VALUES (now(), 1001, '/api/payment', 200, 30);
-- 这条 INSERT 会等到 node1 和 node2 都写入成功才返回

-- 对应的读取也要设置 Quorum，确保读到的是已确认的数据
SET select_sequential_consistency = 1;
SELECT * FROM api_logs_local WHERE api_path = '/api/payment';
```

**Quorum 的代价**：
- 写入延迟增加（要等多个副本确认）
- 如果副本数不够（比如 2 副本设 quorum=2，挂了一个就无法写入）
- 建议只对关键业务表开启，日志类数据用默认的异步复制即可

### ClickHouse Keeper：替代 ZooKeeper

从 ClickHouse 21.8 开始，官方提供了 **ClickHouse Keeper** 作为 ZooKeeper 的替代品：

| 对比项 | ZooKeeper | ClickHouse Keeper |
|--------|-----------|-------------------|
| 语言 | Java | C++（ClickHouse 内置） |
| 部署 | 独立安装，需要 JVM | ClickHouse 自带，零额外依赖 |
| 性能 | 够用 | 更快（尤其是大量 watch 场景） |
| 运维 | 需要单独监控 JVM 内存、GC | 和 ClickHouse 统一运维 |
| 兼容性 | — | 100% 兼容 ZooKeeper 协议 |

**配置 ClickHouse Keeper**（替换 ZooKeeper 配置）：

```xml
<clickhouse>
    <keeper_server>
        <tcp_port>9181</tcp_port>
        <server_id>1</server_id>
        <!-- 数据目录 -->
        <log_storage_path>/var/lib/clickhouse/coordination/log</log_storage_path>
        <snapshot_storage_path>/var/lib/clickhouse/coordination/snapshots</snapshot_storage_path>

        <coordination_settings>
            <operation_timeout_ms>10000</operation_timeout_ms>
            <session_timeout_ms>30000</session_timeout_ms>
        </coordination_settings>

        <!-- Keeper 集群配置（至少 3 个节点） -->
        <raft_configuration>
            <server>
                <id>1</id>
                <hostname>node1</hostname>
                <port>9234</port>
            </server>
            <server>
                <id>2</id>
                <hostname>node2</hostname>
                <port>9234</port>
            </server>
            <server>
                <id>3</id>
                <hostname>node3</hostname>
                <port>9234</port>
            </server>
        </raft_configuration>
    </keeper_server>

    <!-- 让 ClickHouse 使用 Keeper 而不是 ZooKeeper -->
    <zookeeper>
        <node>
            <host>node1</host>
            <port>9181</port>
        </node>
        <node>
            <host>node2</host>
            <port>9181</port>
        </node>
        <node>
            <host>node3</host>
            <port>9181</port>
        </node>
    </zookeeper>
</clickhouse>
```

**新项目直接用 ClickHouse Keeper，老项目可以在线迁移。**

### .NET 代码：副本健康检查服务

```csharp
public class ReplicaHealthService
{
    private readonly string _connectionString;

    public ReplicaHealthService(string connectionString)
    {
        _connectionString = connectionString;
    }

    // 检查所有副本表的同步状态
    public async Task<List<ReplicaHealth>> CheckHealthAsync()
    {
        using var connection = new ClickHouseConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT
                database,
                table,
                replica_name,
                is_leader,
                is_readonly,
                absolute_delay,
                queue_size,
                inserts_in_queue,
                merges_in_queue,
                log_pointer,
                total_replicas,
                active_replicas
            FROM system.replicas";

        var results = new List<ReplicaHealth>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var health = new ReplicaHealth
            {
                Database = reader.GetString(0),
                Table = reader.GetString(1),
                ReplicaName = reader.GetString(2),
                IsLeader = reader.GetBoolean(3),
                IsReadonly = reader.GetBoolean(4),
                AbsoluteDelay = reader.GetInt64(5),
                QueueSize = reader.GetInt64(6),
                InsertsInQueue = reader.GetInt64(7),
                MergesInQueue = reader.GetInt64(8),
                LogPointer = reader.GetInt64(9),
                TotalReplicas = reader.GetInt32(10),
                ActiveReplicas = reader.GetInt32(11)
            };

            // 判断健康状态
            health.Status = health switch
            {
                { IsReadonly: true } => "CRITICAL",       // 只读 = 有问题
                { ActiveReplicas: var a, TotalReplicas: var t } when a < t => "WARNING",  // 有副本离线
                { AbsoluteDelay: > 300 } => "WARNING",    // 同步延迟超过 5 分钟
                { QueueSize: > 100 } => "WARNING",        // 同步队列积压
                _ => "OK"
            };

            results.Add(health);
        }
        return results;
    }
}

public class ReplicaHealth
{
    public string Database { get; init; }
    public string Table { get; init; }
    public string ReplicaName { get; init; }
    public bool IsLeader { get; init; }
    public bool IsReadonly { get; init; }
    public long AbsoluteDelay { get; init; }
    public long QueueSize { get; init; }
    public long InsertsInQueue { get; init; }
    public long MergesInQueue { get; init; }
    public long LogPointer { get; init; }
    public int TotalReplicas { get; init; }
    public int ActiveReplicas { get; init; }
    public string Status { get; set; }
}
```

### 关键点

1. **ZooKeeper 不传数据**：它只存操作日志（几 KB），实际数据通过节点间 HTTP 直连传输
2. **所有副本都可读写**：不是 MySQL 那种主从分离，Leader 只负责协调 MERGE
3. **同步默认是异步的**：写入一个副本就返回成功，需要强一致用 `insert_quorum`
4. **新项目用 ClickHouse Keeper**：零额外依赖，性能更好
5. **ZooKeeper/Keeper 本身也要高可用**：至少 3 节点，允许挂 1 个

### 验证

```sql
-- 模拟故障：在 node2 上停止 ClickHouse
-- systemctl stop clickhouse-server  (在 node2 上执行)

-- 在 node1 上写入数据
INSERT INTO api_logs_local VALUES (now(), 1001, '/api/test', 200, 10);

-- 查询分布式表，仍然正常（自动跳过 node2）
SELECT count() FROM api_logs_distributed;

-- 重启 node2
-- systemctl start clickhouse-server  (在 node2 上执行)

-- 等待几秒后，检查 node2 是否自动同步
-- 在 node2 上执行：
SELECT count() FROM api_logs_local WHERE api_path = '/api/test';
-- 预期：1（数据已自动同步）

-- 检查同步延迟
SELECT replica_name, absolute_delay, queue_size
FROM system.replicas
WHERE table = 'api_logs_local';
-- absolute_delay = 0 表示完全同步
```

---

## 14. 集群拓扑设计与管理

### 业务场景

你要为公司部署生产环境的 ClickHouse 集群。CTO 问了三个问题：
1. 需要几台机器？
2. 怎么规划分片和副本？
3. 以后数据量翻倍了怎么扩容？

### 常见集群拓扑

#### 拓扑 1：1 分片 × 3 副本（小数据量，高可用优先）

```
适用场景：数据量 < 5 TB，查询不密集，但不能丢数据
机器数量：3 台（+ 3 台 Keeper）

┌──────────────────────────┐
│        Shard 1           │
│  ┌──────┐┌──────┐┌──────┐│
│  │node1 ││node2 ││node3 ││
│  │副本1 ││副本2 ││副本3 ││
│  └──────┘└──────┘└──────┘│
└──────────────────────────┘

优点：任意 2 台宕机仍可用，数据安全性最高
缺点：没有水平扩展能力，3 台机器存的是同样的数据
```

#### 拓扑 2：2 分片 × 2 副本（最常见的生产配置）

```
适用场景：数据量 5-50 TB，需要扩展能力和高可用
机器数量：4 台（+ 3 台 Keeper）

┌─────────────┐  ┌─────────────┐
│   Shard 1   │  │   Shard 2   │
│ ┌────┐┌────┐│  │ ┌────┐┌────┐│
│ │ n1 ││ n2 ││  │ │ n3 ││ n4 ││
│ └────┘└────┘│  │ └────┘└────┘│
└─────────────┘  └─────────────┘

优点：存储容量翻倍，查询并行度翻倍，每个分片允许挂 1 台
缺点：同一分片 2 台都挂了就丢数据
```

#### 拓扑 3：3 分片 × 2 副本（大规模数据）

```
适用场景：数据量 > 50 TB，高并发查询
机器数量：6 台（+ 3 台 Keeper）

┌─────────┐  ┌─────────┐  ┌─────────┐
│ Shard 1 │  │ Shard 2 │  │ Shard 3 │
│ ┌──┐┌──┐│  │ ┌──┐┌──┐│  │ ┌──┐┌──┐│
│ │n1││n2││  │ │n3││n4││  │ │n5││n6││
│ └──┘└──┘│  │ └──┘└──┘│  │ └──┘└──┘│
└─────────┘  └─────────┘  └─────────┘

优点：存储容量 ×3，查询并行度 ×3
缺点：机器多，运维复杂度高
```

#### 拓扑 4：Keeper 复用（省机器的方案）

小团队可以把 ClickHouse Keeper 和 ClickHouse Server 部署在同一台机器上：

```
适用场景：预算有限，4 台机器搞定一切
机器数量：4 台

node1: ClickHouse Server + Keeper
node2: ClickHouse Server + Keeper
node3: ClickHouse Server + Keeper
node4: ClickHouse Server（纯计算节点）

集群：2 分片 × 2 副本
Keeper：3 节点（node1/2/3）
```

**注意**：生产环境如果数据量大、写入密集，建议 Keeper 独立部署，避免资源争抢。

### 如何选择拓扑

```
数据量 < 5 TB，查询 QPS < 100
  → 1 分片 × 2 副本（2 台 ClickHouse + 3 台 Keeper，Keeper 可复用其中 2 台）

数据量 5-50 TB，查询 QPS 100-1000
  → 2 分片 × 2 副本（4 台 ClickHouse + 3 台 Keeper）

数据量 > 50 TB，查询 QPS > 1000
  → 3+ 分片 × 2 副本（按需扩展）

数据极其重要，不允许任何丢失
  → N 分片 × 3 副本
```

### 集群扩容：添加新分片

数据量翻倍了，需要从 2 分片扩到 3 分片：

**步骤 1：准备新节点**

在 node5、node6 上安装 ClickHouse，配置 macros：

```xml
<!-- node5 -->
<macros>
    <shard>03</shard>
    <replica>node5</replica>
</macros>

<!-- node6 -->
<macros>
    <shard>03</shard>
    <replica>node6</replica>
</macros>
```

**步骤 2：更新集群配置**

在所有节点的 `cluster.xml` 中添加新分片：

```xml
<my_cluster>
    <!-- 原有的 shard 1 和 shard 2 保持不变 -->
    <shard>...</shard>
    <shard>...</shard>
    <!-- 新增 shard 3 -->
    <shard>
        <internal_replication>true</internal_replication>
        <replica>
            <host>node5</host>
            <port>9000</port>
        </replica>
        <replica>
            <host>node6</host>
            <port>9000</port>
        </replica>
    </shard>
</my_cluster>
```

**步骤 3：在新节点上创建表**

```sql
-- 用 IF NOT EXISTS 避免在已有表的节点上报错
CREATE TABLE IF NOT EXISTS api_logs_local ON CLUSTER my_cluster (
    -- 和现有表结构完全一致
    timestamp DateTime,
    user_id UInt64,
    api_path String,
    status_code UInt16,
    response_time_ms UInt32
) ENGINE = ReplicatedMergeTree('/clickhouse/tables/{shard}/api_logs', '{replica}')
PARTITION BY toYYYYMMDD(timestamp)
ORDER BY (api_path, timestamp);
```

**步骤 4：重新加载配置**

```sql
-- 在所有节点上执行，不需要重启
SYSTEM RELOAD CONFIG;
```

**步骤 5：旧数据需要重新平衡吗？**

扩容后，新写入的数据会自动分散到 3 个分片。但旧数据仍然在原来的 2 个分片上。

**绝大多数情况下，不需要迁移旧数据。** 原因：

1. **查询不受影响**：Distributed 表查询时并行扫描所有分片，shard 1 数据多一点，查询时间也就多几十毫秒
2. **自然平衡**：如果表设了 TTL（比如保留 90 天），旧数据会自动过期删除，90 天后各分片数据量自然趋于均衡
3. **迁移成本高、风险大**：搬数据不是原子操作，搬的过程中还有新数据写入，很容易出问题

```
扩容前（2 分片）：
  Shard 1: ████████████ 50 TB（旧数据 + 新数据）
  Shard 2: ████████████ 50 TB（旧数据 + 新数据）

扩容后（3 分片），第 1 天：
  Shard 1: ████████████ 50 TB（旧数据还在）
  Shard 2: ████████████ 50 TB（旧数据还在）
  Shard 3: █            1 TB（只有新数据）

扩容后，90 天（TTL 过期后）：
  Shard 1: ████████  33 TB（自然平衡）
  Shard 2: ████████  33 TB（自然平衡）
  Shard 3: ████████  33 TB（自然平衡）
```

**只有一种情况需要迁移：某个分片磁盘真的快满了，等不到 TTL 自然过期。**

这时候的正确做法是按分区搬运。但要注意一个陷阱——分区键和分片键通常不是同一个字段：

```
本教程的表：
  分区键：toYYYYMMDD(timestamp)  → 按天分区
  分片键：cityHash64(user_id)    → 按用户哈希分片
```

如果你把 shard 1 的 `20260101` 分区整个搬到 shard 3，这些数据的 `user_id` 哈希值本来对应 shard 1，搬过去后分片键语义就乱了。虽然 Distributed 表全分片扫描时不会丢数据，但如果有人依赖分片键做单分片查询优化，就会漏数据。

**紧急腾磁盘的安全方案：直接删除最老的分区**

```sql
-- 查看各分区占用空间
SELECT
    partition,
    count() AS parts,
    sum(rows) AS rows,
    formatReadableSize(sum(bytes_on_disk)) AS size
FROM system.parts
WHERE table = 'api_logs_local' AND active = 1
GROUP BY partition
ORDER BY partition;

-- 如果最老的数据可以丢弃（日志类数据通常可以），直接删除
ALTER TABLE api_logs_local DROP PARTITION '20260101';
-- 原子操作，瞬间释放磁盘空间
```

如果数据不能丢弃，需要真正搬运，那就用 FREEZE + rsync + ATTACH：

```sql
-- 1. 在 shard 1 上冻结分区（创建硬链接快照，不影响写入）
ALTER TABLE api_logs_local FREEZE PARTITION '20260101';

-- 2. 将快照文件 rsync 到 shard 3 的 detached 目录
-- rsync -av /var/lib/clickhouse/shadow/1/data/default/api_logs_local/20260101/ \
--   node5:/var/lib/clickhouse/data/default/api_logs_local/detached/20260101/

-- 3. 在 shard 3 上挂载
ALTER TABLE api_logs_local ATTACH PARTITION '20260101';

-- 4. 验证行数一致后，从 shard 1 删除
ALTER TABLE api_logs_local DROP PARTITION '20260101';

-- 5. 清理快照
-- rm -rf /var/lib/clickhouse/shadow/1/
```

但要清楚：**这样做之后，这批数据的分片键语义已经不对了。** 对于日志分析场景（全表扫描为主）影响不大；对于需要按分片键精确路由的场景，要评估影响。

### ON CLUSTER DDL：集群级别的表操作

在集群环境中，表结构变更需要在所有节点上同步执行：

```sql
-- ✅ 正确：使用 ON CLUSTER，所有节点同步执行
ALTER TABLE api_logs_local ON CLUSTER my_cluster
    ADD COLUMN request_body String DEFAULT '';

-- ❌ 错误：只在一个节点上执行，其他节点表结构不一致
ALTER TABLE api_logs_local
    ADD COLUMN request_body String DEFAULT '';
-- 后果：查询分布式表时报错 "Column request_body not found on node3"
```

常用的 ON CLUSTER 操作：

```sql
-- 添加列
ALTER TABLE api_logs_local ON CLUSTER my_cluster
    ADD COLUMN region String DEFAULT 'unknown';

-- 删除列
ALTER TABLE api_logs_local ON CLUSTER my_cluster
    DROP COLUMN region;

-- 修改 TTL
ALTER TABLE api_logs_local ON CLUSTER my_cluster
    MODIFY TTL timestamp + INTERVAL 90 DAY;

-- 删除表
DROP TABLE api_logs_local ON CLUSTER my_cluster;
DROP TABLE api_logs_distributed ON CLUSTER my_cluster;
```

### .NET 代码：集群管理服务

```csharp
public class ClusterManagementService
{
    private readonly string _connectionString;

    public ClusterManagementService(string connectionString)
    {
        _connectionString = connectionString;
    }

    // 查看集群拓扑
    public async Task<List<ClusterNode>> GetTopologyAsync(string clusterName)
    {
        using var connection = new ClickHouseConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT
                shard_num,
                replica_num,
                host_name,
                port,
                is_local
            FROM system.clusters
            WHERE cluster = {clusterName:String}
            ORDER BY shard_num, replica_num";

        command.Parameters.AddWithValue("clusterName", clusterName);

        var nodes = new List<ClusterNode>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            nodes.Add(new ClusterNode
            {
                ShardNum = reader.GetInt32(0),
                ReplicaNum = reader.GetInt32(1),
                HostName = reader.GetString(2),
                Port = reader.GetInt32(3),
                IsLocal = reader.GetBoolean(4)
            });
        }
        return nodes;
    }

    // 检查各分片数据分布
    public async Task<List<ShardStats>> GetDataDistributionAsync()
    {
        using var connection = new ClickHouseConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT
                hostName() AS node,
                database,
                table,
                sum(rows) AS total_rows,
                formatReadableSize(sum(bytes_on_disk)) AS disk_size,
                count() AS part_count
            FROM clusterAllReplicas('my_cluster', system.parts)
            WHERE active = 1
              AND database = 'default'
            GROUP BY node, database, table
            ORDER BY node";

        var stats = new List<ShardStats>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            stats.Add(new ShardStats
            {
                Node = reader.GetString(0),
                Database = reader.GetString(1),
                Table = reader.GetString(2),
                TotalRows = reader.GetInt64(3),
                DiskSize = reader.GetString(4),
                PartCount = reader.GetInt64(5)
            });
        }
        return stats;
    }
}

public class ClusterNode
{
    public int ShardNum { get; init; }
    public int ReplicaNum { get; init; }
    public string HostName { get; init; }
    public int Port { get; init; }
    public bool IsLocal { get; init; }
}

public class ShardStats
{
    public string Node { get; init; }
    public string Database { get; init; }
    public string Table { get; init; }
    public long TotalRows { get; init; }
    public string DiskSize { get; init; }
    public long PartCount { get; init; }
}
```

### 关键点

1. **扩容不需要停机**：添加新分片后 `SYSTEM RELOAD CONFIG` 即可，不需要重启
2. **旧数据通常不需要迁移**：等 TTL 自然过期即可平衡，只有磁盘快满了才考虑手动搬运
3. **所有 DDL 用 ON CLUSTER**：否则节点间表结构不一致，查询会报错
4. **Keeper 复用要谨慎**：写入密集时 Keeper 和 Server 争抢 CPU/内存，建议独立部署

---

## 15. 监控与运维

### 业务场景

集群跑了半年，某天突然查询变慢了。你打开监控一看——什么监控都没有。只能盲猜是磁盘满了、还是某个节点挂了、还是有人跑了个全表扫描。

**没有监控的集群，就像没有仪表盘的车——迟早出事。**

### 必须监控的 5 个指标

#### 指标 1：副本同步延迟（最重要）

```sql
-- 延迟超过 5 分钟的副本 = 数据可能不一致
SELECT
    database,
    table,
    replica_name,
    absolute_delay AS delay_seconds,
    queue_size,
    inserts_in_queue,
    merges_in_queue
FROM system.replicas
WHERE absolute_delay > 300
   OR queue_size > 50;
```

**告警阈值**：
- `absolute_delay > 300`（5 分钟）→ WARNING
- `absolute_delay > 3600`（1 小时）→ CRITICAL
- `is_readonly = 1` → CRITICAL（副本进入只读模式，通常是 ZK 连接断了）

#### 指标 2：慢查询

```sql
SELECT
    query_id,
    user,
    substring(query, 1, 200) AS query_preview,
    query_duration_ms,
    read_rows,
    formatReadableSize(read_bytes) AS read_size,
    memory_usage
FROM system.query_log
WHERE type = 'QueryFinish'
  AND query_duration_ms > 5000
  AND event_date = today()
ORDER BY query_duration_ms DESC
LIMIT 10;
```

#### 指标 3：磁盘使用

```sql
-- 按表统计磁盘占用
SELECT
    database,
    table,
    formatReadableSize(sum(bytes_on_disk)) AS disk_size,
    sum(rows) AS total_rows,
    count() AS part_count,
    formatReadableSize(sum(primary_key_bytes_in_memory)) AS pk_memory
FROM system.parts
WHERE active = 1
GROUP BY database, table
ORDER BY sum(bytes_on_disk) DESC;

-- 磁盘剩余空间
SELECT
    name,
    path,
    formatReadableSize(free_space) AS free,
    formatReadableSize(total_space) AS total,
    round(free_space / total_space * 100, 1) AS free_percent
FROM system.disks;
```

**告警阈值**：
- `free_percent < 20%` → WARNING
- `free_percent < 10%` → CRITICAL（ClickHouse 磁盘满了会拒绝写入）

#### 指标 4：合并（Merge）状态

```sql
-- 正在进行的合并操作
SELECT
    database,
    table,
    elapsed,
    progress,
    num_parts,
    formatReadableSize(total_size_bytes_compressed) AS size,
    formatReadableSize(memory_usage) AS memory
FROM system.merges;

-- 如果 parts 数量过多，说明合并跟不上写入速度
SELECT
    database,
    table,
    count() AS part_count
FROM system.parts
WHERE active = 1
GROUP BY database, table
HAVING part_count > 300
ORDER BY part_count DESC;
```

**告警阈值**：
- `part_count > 300` → WARNING（合并跟不上，查询会变慢）
- `part_count > 3000` → CRITICAL（可能触发 "Too many parts" 错误，拒绝写入）

#### 指标 5：连接与查询并发

```sql
-- 当前正在执行的查询
SELECT
    query_id,
    user,
    elapsed,
    read_rows,
    formatReadableSize(memory_usage) AS memory,
    substring(query, 1, 100) AS query_preview
FROM system.processes
ORDER BY elapsed DESC;

-- 当前连接数
SELECT
    metric,
    value
FROM system.metrics
WHERE metric IN ('TCPConnection', 'HTTPConnection', 'InterserverConnection');
```

### Prometheus + Grafana 监控方案

ClickHouse 内置 Prometheus 端点，开箱即用：

```xml
<!-- /etc/clickhouse-server/config.d/prometheus.xml -->
<clickhouse>
    <prometheus>
        <endpoint>/metrics</endpoint>
        <port>9363</port>
        <metrics>true</metrics>
        <events>true</events>
        <asynchronous_metrics>true</asynchronous_metrics>
    </prometheus>
</clickhouse>
```

Prometheus 配置：

```yaml
# prometheus.yml
scrape_configs:
  - job_name: 'clickhouse'
    static_configs:
      - targets:
        - 'node1:9363'
        - 'node2:9363'
        - 'node3:9363'
        - 'node4:9363'
    scrape_interval: 15s
```

关键的 Prometheus 指标：

| 指标 | 含义 | 告警条件 |
|------|------|---------|
| `ClickHouseMetrics_ReplicasMaxAbsoluteDelay` | 最大副本延迟 | > 300s |
| `ClickHouseMetrics_MaxPartCountForPartition` | 单分区最大 part 数 | > 300 |
| `ClickHouseAsyncMetrics_DiskUsed_default` | 磁盘使用量 | > 80% |
| `ClickHouseProfileEvents_FailedQuery` | 失败查询数 | 突增 |
| `ClickHouseProfileEvents_ZooKeeperHardwareExceptions` | ZK 连接异常 | > 0 |

### .NET 代码：综合健康检查

```csharp
public class ClusterHealthCheckService
{
    private readonly string _connectionString;

    public ClusterHealthCheckService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<ClusterHealthReport> RunFullCheckAsync()
    {
        using var connection = new ClickHouseConnection(_connectionString);
        await connection.OpenAsync();

        var report = new ClusterHealthReport
        {
            CheckTime = DateTime.UtcNow,
            ReplicaIssues = await CheckReplicasAsync(connection),
            DiskIssues = await CheckDiskAsync(connection),
            PartIssues = await CheckPartsAsync(connection)
        };

        report.OverallStatus = report switch
        {
            { ReplicaIssues.Count: > 0 } => "CRITICAL",
            { DiskIssues.Count: > 0 } => "WARNING",
            { PartIssues.Count: > 0 } => "WARNING",
            _ => "OK"
        };

        return report;
    }

    private async Task<List<string>> CheckReplicasAsync(ClickHouseConnection conn)
    {
        var issues = new List<string>();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT replica_name, table, absolute_delay, is_readonly
            FROM system.replicas
            WHERE absolute_delay > 300 OR is_readonly = 1";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(0);
            var table = reader.GetString(1);
            var delay = reader.GetInt64(2);
            var readOnly = reader.GetBoolean(3);

            if (readOnly)
                issues.Add($"CRITICAL: {name}/{table} is READONLY");
            else
                issues.Add($"WARNING: {name}/{table} delay={delay}s");
        }
        return issues;
    }

    private async Task<List<string>> CheckDiskAsync(ClickHouseConnection conn)
    {
        var issues = new List<string>();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT name, path,
                   round(free_space / total_space * 100, 1) AS free_pct
            FROM system.disks
            WHERE free_space / total_space < 0.2";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var disk = reader.GetString(0);
            var freePct = reader.GetDouble(2);
            issues.Add($"WARNING: Disk {disk} only {freePct}% free");
        }
        return issues;
    }

    private async Task<List<string>> CheckPartsAsync(ClickHouseConnection conn)
    {
        var issues = new List<string>();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT database, table, count() AS cnt
            FROM system.parts WHERE active = 1
            GROUP BY database, table HAVING cnt > 300";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var db = reader.GetString(0);
            var table = reader.GetString(1);
            var count = reader.GetInt64(2);
            issues.Add($"WARNING: {db}.{table} has {count} active parts");
        }
        return issues;
    }
}

public class ClusterHealthReport
{
    public DateTime CheckTime { get; init; }
    public string OverallStatus { get; set; }
    public List<string> ReplicaIssues { get; init; }
    public List<string> DiskIssues { get; init; }
    public List<string> PartIssues { get; init; }
}
```

### 关键点

1. **副本延迟是第一优先级**：延迟意味着数据不一致，必须立即排查
2. **磁盘满 = 集群停写**：ClickHouse 磁盘满了直接拒绝 INSERT，务必提前告警
3. **Parts 过多 = 查询变慢**：如果合并跟不上写入，考虑降低写入频率或增加 `max_threads`
4. **用 Prometheus + Grafana**：比手动查 system 表高效 100 倍

### 常见运维操作速查

```sql
-- 手动触发合并（parts 太多时）
OPTIMIZE TABLE api_logs_local FINAL;
-- 注意：FINAL 会强制合并所有 parts，非常消耗资源，低峰期执行

-- 停止/恢复后台合并（紧急情况下释放资源）
SYSTEM STOP MERGES api_logs_local;
SYSTEM START MERGES api_logs_local;

-- 停止/恢复副本同步（维护窗口期）
SYSTEM STOP FETCHES api_logs_local;
SYSTEM START FETCHES api_logs_local;

-- 清理过期数据（TTL 没自动清理时手动触发）
ALTER TABLE api_logs_local MATERIALIZE TTL;

-- 查看 ZooKeeper/Keeper 连接状态
SELECT * FROM system.zookeeper WHERE path = '/clickhouse';

-- 杀掉长时间运行的查询
KILL QUERY WHERE query_id = 'xxx';
```

---

## 总结

本章覆盖了 ClickHouse 集群的核心知识：

**架构层面**：分片解决容量问题，副本解决可用性问题，两者正交组合。Distributed 表是路由层，不存数据。

**数据同步**：ZooKeeper/Keeper 只存操作日志，实际数据通过节点间 HTTP 直连传输。同步默认异步，需要强一致用 Quorum 写入。

**拓扑选择**：小数据量用 1 分片多副本，大数据量按需增加分片。新项目用 ClickHouse Keeper 替代 ZooKeeper。

**运维要点**：监控副本延迟、磁盘使用、Parts 数量三个核心指标，用 Prometheus + Grafana 建立告警体系。

---

## 参考资料

- [ClickHouse 官方文档 - 副本与分片](https://clickhouse.com/docs/en/architecture/replication)
- [ClickHouse Keeper 文档](https://clickhouse.com/docs/en/guides/sre/keeper/clickhouse-keeper)
- [ClickHouse.Client NuGet 包](https://www.nuget.org/packages/ClickHouse.Client/)
- [ClickHouse 集群最佳实践](https://clickhouse.com/docs/en/guides/best-practices)

---

[返回目录](./ClickHouse教程-目录.md)
