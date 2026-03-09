# ClickHouse 教程 - Part 1: 基础入门

[返回目录](./ClickHouse教程-目录.md)

---

## 1. ClickHouse 是什么

### 业务场景

你的 .NET 系统每天产生 10 亿条日志，需要实时查询：
- "过去 1 小时内，哪个 API 接口错误率最高？"
- "昨天访问量 TOP 10 的用户是谁？"
- "本月每天的活跃用户数趋势如何？"

用 Elasticsearch 查询这些数据：
- 简单聚合还行，但高基数字段（如 user_id）的 `terms` 聚合越来越慢
- 多维度交叉分析（按接口 × 状态码 × 时间粒度）DSL 写起来极其复杂
- 存储成本高——倒排索引 + doc_values + _source 三份数据，磁盘占用是原始数据的 3-5 倍
- 想做个同比环比？ES 没有窗口函数，只能应用层拼接

### 问题

Elasticsearch 的设计目标是**全文搜索**，被拿来做日志分析是"能用但不够好"：
- 聚合查询基于 doc_values，数据量过亿后性能急剧下降
- DSL 语法复杂，一个多层嵌套聚合动辄几十行 JSON
- 不支持 JOIN、窗口函数、子查询等 SQL 分析能力
- 存储效率低，同样的数据 ES 占用空间是 ClickHouse 的 5-10 倍

### 方案

ClickHouse 是一个**列式存储**的 OLAP 数据库，专为分析场景设计：
- 列式存储：只读取需要的列，减少 I/O
- 向量化执行：利用 CPU SIMD 指令加速计算
- 数据压缩：同一列数据类型相同，压缩比高（通常 10:1）
- 分布式查询：支持水平扩展，处理 PB 级数据

### 性能对比

**类比**：查询"过去 7 天每天的订单总额"

- **Elasticsearch**：先把数据从 doc_values 列式结构中取出，通过 `date_histogram` + `sum` 聚合计算。看起来也是列式？但 ES 的聚合走的是 Lucene 的 doc_values，需要逐个 segment 遍历再合并，且无法利用 CPU SIMD 指令
- **ClickHouse**：直接读取"日期"和"金额"两列的压缩数据块，用向量化引擎批量计算，一次处理 8192 行

**原理**：

| 维度 | Elasticsearch | ClickHouse |
|------|--------------|------------|
| 存储模型 | 倒排索引 + doc_values + _source | 列式存储 + 稀疏索引 |
| 1 亿行聚合查询 | 3-10 秒（取决于基数） | 50-200 毫秒 |
| 10 亿行磁盘占用 | ~500 GB（3-5x 膨胀） | ~50 GB（10:1 压缩） |
| 多维分析 | DSL 嵌套聚合，复杂难维护 | 标准 SQL，GROUP BY 随便写 |
| JOIN 能力 | 几乎没有 | 支持多种 JOIN |

**代码示例**（.NET 客户端）：

```csharp
using ClickHouse.Client.ADO;

// 连接 ClickHouse
using var connection = new ClickHouseConnection("Host=localhost;Port=9000;Database=default");
await connection.OpenAsync();

// 查询过去 7 天每天的订单总额
var command = connection.CreateCommand();
command.CommandText = @"
    SELECT
        toDate(order_time) AS date,
        sum(amount) AS total_amount
    FROM orders
    WHERE order_time >= now() - INTERVAL 7 DAY
    GROUP BY date
    ORDER BY date";

using var reader = await command.ExecuteReaderAsync();
while (await reader.ReadAsync())
{
    var date = reader.GetDateTime(0);
    var amount = reader.GetDecimal(1);
    Console.WriteLine($"{date:yyyy-MM-dd}: {amount:C}");
}
// 查询时间：~50ms（1 亿行数据）
```

### 关键点

1. **ClickHouse 不是 ES 的替代品**：ES 擅长全文搜索和模糊匹配，ClickHouse 擅长聚合分析。常见架构是两者并存——ES 负责搜索，ClickHouse 负责报表
2. **不支持高频更新/删除**：适合写入后不再修改的数据（日志、事件、埋点）
3. **查询必须带时间范围**：避免全表扫描，这点和 ES 一样

### 验证

安装 ClickHouse 后，运行以下测试：

```sql
-- 创建测试表（1 亿行数据）
CREATE TABLE test_performance (
    id UInt64,
    timestamp DateTime,
    value Float64
) ENGINE = MergeTree()
ORDER BY timestamp;

-- 插入 1 亿行测试数据
INSERT INTO test_performance
SELECT
    number AS id,
    now() - INTERVAL number SECOND AS timestamp,
    rand() / 1000000000 AS value
FROM numbers(100000000);

-- 测试查询性能
SELECT
    toStartOfHour(timestamp) AS hour,
    avg(value) AS avg_value
FROM test_performance
WHERE timestamp >= now() - INTERVAL 24 HOUR
GROUP BY hour
ORDER BY hour;
-- 预期查询时间：< 100ms
```

---

## 2. 安装与快速上手

### 业务场景

你需要在开发环境快速搭建 ClickHouse，用于测试日志分析功能。

### 方案

使用 Docker 快速部署单机版 ClickHouse。

### 实现

**步骤 1：安装 Docker**

```bash
# Windows: 下载 Docker Desktop
# https://www.docker.com/products/docker-desktop

# 验证安装
docker --version
```

**步骤 2：启动 ClickHouse 容器**

```bash
# 拉取镜像
docker pull clickhouse/clickhouse-server:latest

# 启动容器
docker run -d \
  --name clickhouse-server \
  -p 8123:8123 \
  -p 9000:9000 \
  -v clickhouse-data:/var/lib/clickhouse \
  clickhouse/clickhouse-server:latest

# 验证运行状态
docker ps | grep clickhouse
```

**步骤 3：安装 .NET 客户端**

```bash
# 安装 NuGet 包
dotnet add package ClickHouse.Client
```

**步骤 4：测试连接**

```csharp
using ClickHouse.Client.ADO;

var connectionString = "Host=localhost;Port=9000;Database=default;User=default;Password=";
using var connection = new ClickHouseConnection(connectionString);

try
{
    await connection.OpenAsync();
    Console.WriteLine("✅ 连接成功");

    // 测试查询
    var command = connection.CreateCommand();
    command.CommandText = "SELECT version()";
    var version = await command.ExecuteScalarAsync();
    Console.WriteLine($"ClickHouse 版本: {version}");
}
catch (Exception ex)
{
    Console.WriteLine($"❌ 连接失败: {ex.Message}");
}
```

### 关键点

1. **端口说明**：
   - 8123：HTTP 接口（用于 Web UI 和 REST API）
   - 9000：Native 接口（用于客户端连接，性能更好）

2. **数据持久化**：使用 Docker Volume 避免容器重启后数据丢失

3. **生产环境**：不要用 Docker 单机部署，使用集群模式

### 验证

访问 ClickHouse Web UI：
```
http://localhost:8123/play
```

执行测试查询：
```sql
SELECT 'Hello, ClickHouse!' AS message;
```

---

## 3. 核心概念

### 3.1 表引擎（Table Engine）

#### 业务场景

你需要存储用户行为日志，要求：
- 按时间范围快速查询
- 数据按日期自动过期（保留 30 天）
- 支持实时写入

#### 问题

ClickHouse 有 30+ 种表引擎，如何选择？

#### 方案

**MergeTree 系列**是最常用的引擎，适合 90% 的场景。

#### 实现

```sql
-- ❌ 错误：使用 Memory 引擎（数据不持久化）
CREATE TABLE user_events_wrong (
    user_id UInt64,
    event_time DateTime,
    event_type String
) ENGINE = Memory;

-- ✅ 正确：使用 MergeTree 引擎
CREATE TABLE user_events (
    user_id UInt64,
    event_time DateTime,
    event_type String,
    properties String
) ENGINE = MergeTree()
PARTITION BY toYYYYMM(event_time)  -- 按月分区
ORDER BY (user_id, event_time)     -- 排序键（影响查询性能）
TTL event_time + INTERVAL 30 DAY;  -- 30 天后自动删除
```

**对应的 .NET 代码**：

```csharp
public class UserEvent
{
    public ulong UserId { get; set; }
    public DateTime EventTime { get; set; }
    public string EventType { get; set; }
    public string Properties { get; set; }
}

public async Task CreateTableAsync()
{
    using var connection = new ClickHouseConnection(connectionString);
    await connection.OpenAsync();

    var command = connection.CreateCommand();
    command.CommandText = @"
        CREATE TABLE IF NOT EXISTS user_events (
            user_id UInt64,
            event_time DateTime,
            event_type String,
            properties String
        ) ENGINE = MergeTree()
        PARTITION BY toYYYYMM(event_time)
        ORDER BY (user_id, event_time)
        TTL event_time + INTERVAL 30 DAY";

    await command.ExecuteNonQueryAsync();
}
```

#### 关键点

1. **ORDER BY 不是索引**：它决定数据在磁盘上的排序方式，影响查询性能
2. **PARTITION BY 主要用于数据管理**：方便删除过期数据。同时 ClickHouse 会自动做分区裁剪（partition pruning）——查询带时间范围条件时，直接跳过无关分区，减少扫描量
3. **TTL 自动清理数据**：避免手动删除，减少运维成本

---

### 3.2 数据类型

#### 业务场景

你需要存储订单数据，包含金额、时间、状态等字段。

#### 问题

ClickHouse 的数据类型与 Elasticsearch 不同，如何映射？

#### 方案

| Elasticsearch | ClickHouse | 说明 |
|---------------|------------|------|
| long | Int64 / UInt64 | 64 位整数，无符号场景用 UInt64 |
| integer | Int32 / UInt32 | 32 位整数 |
| double | Float64 | 双精度浮点 |
| scaled_float | Decimal(P, S) | 固定精度小数（金额等场景） |
| date / date_nanos | DateTime / DateTime64(3) | 秒级 / 毫秒级精度 |
| keyword | String / LowCardinality(String) | 精确匹配字符串，低基数用 LowCardinality |
| text | String | ClickHouse 无全文搜索，仅存储原文 |
| boolean | UInt8 | 0/1 表示布尔值 |
| ip | IPv4 / IPv6 | 原生 IP 类型，比 keyword 省空间 |
| object / nested | Tuple / Nested | 嵌套结构 |

#### 实现

```sql
CREATE TABLE orders (
    order_id UUID,
    user_id UInt64,
    amount Decimal(18, 2),
    order_time DateTime64(3),  -- 毫秒精度
    status Enum8('pending' = 1, 'paid' = 2, 'shipped' = 3, 'completed' = 4),
    metadata String
) ENGINE = MergeTree()
ORDER BY (user_id, order_time);
```

**对应的 .NET 模型**：

```csharp
public class Order
{
    public Guid OrderId { get; set; }
    public ulong UserId { get; set; }
    public decimal Amount { get; set; }
    public DateTime OrderTime { get; set; }
    public OrderStatus Status { get; set; }
    public string Metadata { get; set; }
}

public enum OrderStatus : byte
{
    Pending = 1,
    Paid = 2,
    Shipped = 3,
    Completed = 4
}
```

#### 关键点

1. **LowCardinality 是 ClickHouse 的杀手锏**：ES 的 keyword 字段会建倒排索引，ClickHouse 的 LowCardinality 用字典编码，对低基数字段（状态、地区、类型）查询更快且省空间
2. **DateTime64(3) 用于高精度时间**：日志、事件等需要毫秒精度，对应 ES 的 date 类型
3. **Enum 节省空间**：状态字段用枚举比字符串节省 90% 空间，ES 没有原生枚举类型
4. **Nested 类型慎用**：ClickHouse 的 Nested 和 ES 的 nested 都有性能开销，能展平就展平

---

### 3.3 排序键（ORDER BY）

#### 业务场景

你需要查询"某个用户在某个时间范围内的所有订单"。

#### 问题

如何设计排序键，让查询最快？

#### 方案

**排序键的顺序决定查询性能**：
- 第一列：最常用的过滤条件
- 第二列：第二常用的过滤条件
- 以此类推

#### 实现

```sql
-- ❌ 错误：排序键顺序不合理
CREATE TABLE orders_wrong (
    order_id UUID,
    user_id UInt64,
    order_time DateTime
) ENGINE = MergeTree()
ORDER BY (order_time, user_id);  -- 先按时间排序
-- 查询 "WHERE user_id = 123" 会很慢，因为数据不是按 user_id 聚集的

-- ✅ 正确：根据查询模式设计排序键
CREATE TABLE orders (
    order_id UUID,
    user_id UInt64,
    order_time DateTime
) ENGINE = MergeTree()
ORDER BY (user_id, order_time);  -- 先按 user_id 排序
-- 查询 "WHERE user_id = 123 AND order_time > '2024-01-01'" 很快
```

**对应的 .NET 查询**：

```csharp
// 高效查询：利用排序键
public async Task<List<Order>> GetUserOrdersAsync(ulong userId, DateTime startTime)
{
    using var connection = new ClickHouseConnection(connectionString);
    await connection.OpenAsync();

    var command = connection.CreateCommand();
    command.CommandText = @"
        SELECT order_id, user_id, amount, order_time, status
        FROM orders
        WHERE user_id = {userId:UInt64}
          AND order_time >= {startTime:DateTime}
        ORDER BY order_time DESC
        LIMIT 100";

    command.Parameters.AddWithValue("userId", userId);
    command.Parameters.AddWithValue("startTime", startTime);

    var orders = new List<Order>();
    using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        orders.Add(new Order
        {
            OrderId = reader.GetGuid(0),
            UserId = reader.GetFieldValue<ulong>(1),
            Amount = reader.GetDecimal(2),
            OrderTime = reader.GetDateTime(3),
            Status = (OrderStatus)reader.GetByte(4)
        });
    }
    return orders;
}
```

#### 关键点

1. **排序键不是唯一约束**：可以有重复值
2. **排序键影响压缩率**：相似的数据排在一起，压缩效果更好
3. **排序键最多 3-4 列**：太多列会降低写入性能

### 验证

测试不同排序键的查询性能：

```sql
-- 测试 1：按排序键查询（快）
SELECT count()
FROM orders
WHERE user_id = 12345
  AND order_time >= '2024-01-01';
-- 预期：< 10ms

-- 测试 2：不按排序键查询（慢）
SELECT count()
FROM orders
WHERE order_id = 'xxx-xxx-xxx';
-- 预期：> 1000ms（全表扫描）
```

---

[下一章：Part 2 - 实战场景](./ClickHouse教程-Part2-实战场景.md)
