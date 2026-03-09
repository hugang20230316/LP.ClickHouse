# ClickHouse 教程 - Part 3: 性能优化

[返回目录](./ClickHouse教程-目录.md) | [上一章：Part 2 - 实战场景](./ClickHouse教程-Part2-实战场景.md)

---

## 8. 查询优化技巧

### 业务场景

你的查询很慢，需要优化：
- 查询 1 亿行数据需要 10 秒
- 聚合查询占用大量内存
- 多表 JOIN 性能差

### 问题

ClickHouse 虽然快，但不合理的查询仍然会很慢。

### 方案

遵循 ClickHouse 查询优化最佳实践。

### 实现

#### 优化 1：避免 SELECT *

```sql
-- ❌ 错误：查询所有列
SELECT *
FROM api_logs
WHERE timestamp >= now() - INTERVAL 1 HOUR;
-- 读取所有列，浪费 I/O

-- ✅ 正确：只查询需要的列
SELECT timestamp, api_path, status_code
FROM api_logs
WHERE timestamp >= now() - INTERVAL 1 HOUR;
-- 只读取 3 列，I/O 减少 80%
```

**对应的 .NET 代码**：

```csharp
// ❌ 错误：查询所有列
var command = connection.CreateCommand();
command.CommandText = "SELECT * FROM api_logs WHERE timestamp >= now() - INTERVAL 1 HOUR";

// ✅ 正确：只查询需要的列
command.CommandText = @"
    SELECT timestamp, api_path, status_code
    FROM api_logs
    WHERE timestamp >= now() - INTERVAL 1 HOUR";
```

#### 优化 2：理解 PREWHERE 自动优化

```sql
-- ClickHouse 默认开启 optimize_move_to_prewhere，
-- 会自动把 WHERE 中基于排序键的条件转为 PREWHERE。
-- 所以大多数情况下，你不需要手动写 PREWHERE。

-- 以下两条查询在 ClickHouse 内部执行效果相同：
SELECT api_path, count()
FROM api_logs
WHERE timestamp >= '2024-01-01'
  AND status_code >= 500
GROUP BY api_path;

SELECT api_path, count()
FROM api_logs
PREWHERE timestamp >= '2024-01-01'
WHERE status_code >= 500
GROUP BY api_path;

-- 什么时候需要手动写 PREWHERE？
-- 当自动优化选错了列时（极少见），或者你想强制指定哪个条件先过滤。
-- 可以用 EXPLAIN 查看实际执行计划：
EXPLAIN SYNTAX
SELECT api_path, count()
FROM api_logs
WHERE timestamp >= '2024-01-01'
  AND status_code >= 500
GROUP BY api_path;
-- 输出会显示 ClickHouse 自动把 timestamp 条件移到了 PREWHERE
```

**原理**：
- PREWHERE 在读取数据前先过滤，减少后续列的 I/O
- ClickHouse 默认自动优化（`optimize_move_to_prewhere = 1`），会选择过滤效果最好的列
- 手动写 PREWHERE 的场景很少，了解原理即可

**对应的 .NET 代码**：

```csharp
public async Task<List<ApiErrorStats>> GetErrorStatsAsync(DateTime startTime)
{
    using var connection = new ClickHouseConnection(_connectionString);
    await connection.OpenAsync();

    var command = connection.CreateCommand();
    command.CommandText = @"
        SELECT api_path, count() AS error_count
        FROM api_logs
        PREWHERE timestamp >= {startTime:DateTime64(3)}
        WHERE status_code >= 500
        GROUP BY api_path
        ORDER BY error_count DESC
        LIMIT 20";

    command.Parameters.AddWithValue("startTime", startTime);

    var stats = new List<ApiErrorStats>();
    using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        stats.Add(new ApiErrorStats
        {
            ApiPath = reader.GetString(0),
            ErrorCount = reader.GetInt64(1)
        });
    }
    return stats;
}
```

#### 优化 3：避免大范围 JOIN

```sql
-- ❌ 错误：大表 JOIN 大表
SELECT
    u.user_id,
    u.username,
    count(o.order_id) AS order_count
FROM users u
JOIN orders o ON u.user_id = o.user_id
WHERE o.order_time >= '2024-01-01'
GROUP BY u.user_id, u.username;
-- 两个大表 JOIN，内存占用高

-- ✅ 正确：先聚合再 JOIN
SELECT
    o.user_id,
    u.username,
    o.order_count
FROM (
    SELECT user_id, count() AS order_count
    FROM orders
    WHERE order_time >= '2024-01-01'
    GROUP BY user_id
) o
JOIN users u ON o.user_id = u.user_id;
-- 先聚合减少数据量，再 JOIN
```

**对应的 .NET 代码**：

```csharp
public async Task<List<UserOrderStats>> GetUserOrderStatsAsync(DateTime startTime)
{
    using var connection = new ClickHouseConnection(_connectionString);
    await connection.OpenAsync();

    var command = connection.CreateCommand();
    command.CommandText = @"
        SELECT
            o.user_id,
            u.username,
            o.order_count,
            o.total_amount
        FROM (
            SELECT
                user_id,
                count() AS order_count,
                sum(amount) AS total_amount
            FROM orders
            WHERE order_time >= {startTime:DateTime}
            GROUP BY user_id
        ) o
        JOIN users u ON o.user_id = u.user_id
        ORDER BY o.total_amount DESC
        LIMIT 100";

    command.Parameters.AddWithValue("startTime", startTime);

    var stats = new List<UserOrderStats>();
    using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        stats.Add(new UserOrderStats
        {
            UserId = reader.GetFieldValue<ulong>(0),
            Username = reader.GetString(1),
            OrderCount = reader.GetInt64(2),
            TotalAmount = reader.GetDecimal(3)
        });
    }
    return stats;
}
```

#### 优化 4：使用采样查询

```sql
-- 前提：建表时必须声明 SAMPLE BY，否则 SAMPLE 子句不生效
CREATE TABLE api_logs (
    timestamp DateTime,
    api_path String,
    status_code UInt16,
    response_time_ms UInt32,
    user_id UInt64
) ENGINE = MergeTree()
PARTITION BY toYYYYMMDD(timestamp)
ORDER BY (timestamp, cityHash64(user_id))
SAMPLE BY cityHash64(user_id);  -- 声明采样键

-- ❌ 慢：全表扫描
SELECT avg(response_time_ms)
FROM api_logs
WHERE timestamp >= now() - INTERVAL 7 DAY;
-- 查询 10 亿行数据

-- ✅ 快：采样查询
SELECT avg(response_time_ms)
FROM api_logs SAMPLE 0.1  -- 采样 10%
WHERE timestamp >= now() - INTERVAL 7 DAY;
-- 只查询约 1 亿行数据，结果误差通常 < 3%
-- 适合趋势分析、大盘监控等不需要精确值的场景
```

**对应的 .NET 代码**：

```csharp
public async Task<double> GetAvgResponseTimeAsync(DateTime startTime, double sampleRate = 0.1)
{
    // SAMPLE 子句不支持参数化，但 sampleRate 是内部控制的数值，不是用户输入
    if (sampleRate is <= 0 or > 1)
        throw new ArgumentOutOfRangeException(nameof(sampleRate), "Must be between 0 and 1");

    using var connection = new ClickHouseConnection(_connectionString);
    await connection.OpenAsync();

    var command = connection.CreateCommand();
    command.CommandText = $@"
        SELECT avg(response_time_ms)
        FROM api_logs SAMPLE {sampleRate:F2}
        WHERE timestamp >= {{startTime:DateTime64(3)}}";

    command.Parameters.AddWithValue("startTime", startTime);

    var result = await command.ExecuteScalarAsync();
    return Convert.ToDouble(result);
}
```

### 关键点

1. **只查询需要的列**：减少 I/O
2. **PREWHERE 通常自动优化**：了解原理即可，极少需要手动写
3. **先聚合再 JOIN**：减少 JOIN 数据量
4. **采样查询需要 SAMPLE BY**：建表时声明，适合不需要精确值的场景

### 验证

对比优化前后的查询性能：

```sql
-- 优化前
SELECT * FROM api_logs WHERE timestamp >= now() - INTERVAL 1 HOUR;
-- 查询时间：5000ms

-- 优化后
SELECT timestamp, api_path, status_code
FROM api_logs
PREWHERE timestamp >= now() - INTERVAL 1 HOUR;
-- 查询时间：500ms（快 10 倍）
```

---

## 9. 索引设计策略

### 业务场景

你需要优化查询性能：
- 按用户 ID 查询很慢
- 按商品 ID 查询很慢
- 按多个条件组合查询很慢

### 问题

ClickHouse 的索引与传统数据库不同：
- 没有 B-Tree 索引
- 主键不是唯一约束
- 需要手动设计索引

### 方案

使用 ClickHouse 的稀疏索引和跳数索引。

### 实现

#### 索引 1：稀疏索引（自动创建）

```sql
CREATE TABLE orders (
    order_id UUID,
    user_id UInt64,
    order_time DateTime
) ENGINE = MergeTree()
ORDER BY (user_id, order_time);  -- 自动创建稀疏索引
```

**原理**：
- ClickHouse 每 8192 行创建一个索引标记
- 查询时先用索引定位数据块，再扫描数据块
- 适合范围查询，不适合点查询

**对应的 .NET 查询**：

```csharp
// 高效：利用稀疏索引
public async Task<List<Order>> GetUserOrdersAsync(ulong userId)
{
    using var connection = new ClickHouseConnection(_connectionString);
    await connection.OpenAsync();

    var command = connection.CreateCommand();
    command.CommandText = @"
        SELECT order_id, user_id, order_time, amount
        FROM orders
        WHERE user_id = {userId:UInt64}  -- 利用稀疏索引
        ORDER BY order_time DESC
        LIMIT 100";

    command.Parameters.AddWithValue("userId", userId);

    var orders = new List<Order>();
    using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        orders.Add(new Order
        {
            OrderId = reader.GetGuid(0),
            UserId = reader.GetFieldValue<ulong>(1),
            OrderTime = reader.GetDateTime(2),
            Amount = reader.GetDecimal(3)
        });
    }
    return orders;
}
```

#### 索引 2：跳数索引（手动创建）

```sql
-- 创建表时添加跳数索引
CREATE TABLE orders (
    order_id UUID,
    user_id UInt64,
    product_id UInt64,
    order_time DateTime,
    amount Decimal(18, 2),
    INDEX idx_product_id product_id TYPE minmax GRANULARITY 4
) ENGINE = MergeTree()
ORDER BY (user_id, order_time);

-- 或者在已有表上添加索引
ALTER TABLE orders ADD INDEX idx_product_id product_id TYPE minmax GRANULARITY 4;
ALTER TABLE orders MATERIALIZE INDEX idx_product_id;
```

**索引类型**：

| 类型 | 适用场景 | 示例 |
|------|---------|------|
| minmax | 数值、日期范围查询 | `WHERE product_id BETWEEN 100 AND 200` |
| set | 枚举值查询 | `WHERE status IN ('pending', 'paid')` |
| bloom_filter | 字符串精确匹配 | `WHERE email = 'user@example.com'` |
| ngrambf_v1 | 字符串模糊匹配 | `WHERE title LIKE '%keyword%'` |

**对应的 .NET 代码**：

```csharp
// 创建带跳数索引的表
public async Task CreateTableWithIndexAsync()
{
    using var connection = new ClickHouseConnection(_connectionString);
    await connection.OpenAsync();

    var command = connection.CreateCommand();
    command.CommandText = @"
        CREATE TABLE IF NOT EXISTS orders (
            order_id UUID,
            user_id UInt64,
            product_id UInt64,
            order_time DateTime,
            amount Decimal(18, 2),
            INDEX idx_product_id product_id TYPE minmax GRANULARITY 4
        ) ENGINE = MergeTree()
        ORDER BY (user_id, order_time)";

    await command.ExecuteNonQueryAsync();
}

// 利用跳数索引查询
public async Task<List<Order>> GetProductOrdersAsync(ulong productId)
{
    using var connection = new ClickHouseConnection(_connectionString);
    await connection.OpenAsync();

    var command = connection.CreateCommand();
    command.CommandText = @"
        SELECT order_id, user_id, product_id, order_time, amount
        FROM orders
        WHERE product_id = {productId:UInt64}  -- 利用跳数索引
        ORDER BY order_time DESC
        LIMIT 100";

    command.Parameters.AddWithValue("productId", productId);

    var orders = new List<Order>();
    using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        orders.Add(new Order
        {
            OrderId = reader.GetGuid(0),
            UserId = reader.GetFieldValue<ulong>(1),
            ProductId = reader.GetFieldValue<ulong>(2),
            OrderTime = reader.GetDateTime(3),
            Amount = reader.GetDecimal(4)
        });
    }
    return orders;
}
```

### 关键点

1. **稀疏索引自动创建**：基于 ORDER BY 列
2. **跳数索引需要手动创建**：用于非排序键列
3. **索引不是万能的**：不合理的索引反而降低性能
4. **bloom_filter 索引占用内存**：谨慎使用

### 验证

对比有无索引的查询性能：

```sql
-- 无索引：全表扫描
SELECT count()
FROM orders
WHERE product_id = 12345;
-- 查询时间：5000ms

-- 有索引：跳过不相关的数据块
SELECT count()
FROM orders
WHERE product_id = 12345;
-- 查询时间：50ms（快 100 倍）
```

---

## 10. 分区与 TTL

### 业务场景

你需要管理大量历史数据：
- 日志数据保留 30 天
- 订单数据保留 1 年
- 需要快速删除过期数据

### 问题

传统数据库删除数据很慢：
- DELETE 操作锁表
- 删除大量数据需要很长时间
- 磁盘空间不会立即释放

### 方案

使用 ClickHouse 的分区和 TTL 自动管理数据。

### 实现

#### 方案 1：按时间分区

```sql
CREATE TABLE api_logs (
    timestamp DateTime,
    api_path String,
    status_code UInt16
) ENGINE = MergeTree()
PARTITION BY toYYYYMMDD(timestamp)  -- 按天分区
ORDER BY timestamp;
```

**优点**：
- 删除分区很快（秒级）
- 查询时自动跳过无关分区

**删除过期分区**：

```sql
-- 删除 30 天前的数据
ALTER TABLE api_logs DROP PARTITION '20240101';
```

**对应的 .NET 代码**：

```csharp
public async Task DropOldPartitionsAsync(int daysToKeep = 30)
{
    using var connection = new ClickHouseConnection(_connectionString);
    await connection.OpenAsync();

    // 查询所有分区
    var command = connection.CreateCommand();
    command.CommandText = @"
        SELECT DISTINCT partition
        FROM system.parts
        WHERE table = 'api_logs'
          AND active = 1";

    var partitions = new List<string>();
    using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        partitions.Add(reader.GetString(0));
    }

    // 删除过期分区
    var cutoffDate = DateTime.UtcNow.AddDays(-daysToKeep);
    foreach (var partition in partitions)
    {
        if (DateTime.TryParseExact(partition, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var partitionDate))
        {
            if (partitionDate < cutoffDate)
            {
                var dropCommand = connection.CreateCommand();
                dropCommand.CommandText = $"ALTER TABLE api_logs DROP PARTITION '{partition}'";
                await dropCommand.ExecuteNonQueryAsync();
                Console.WriteLine($"已删除分区: {partition}");
            }
        }
    }
}
```

#### 方案 2：使用 TTL 自动删除

```sql
CREATE TABLE api_logs (
    timestamp DateTime,
    api_path String,
    status_code UInt16
) ENGINE = MergeTree()
PARTITION BY toYYYYMMDD(timestamp)
ORDER BY timestamp
TTL timestamp + INTERVAL 30 DAY;  -- 30 天后自动删除
```

**优点**：
- 自动删除，无需手动维护
- 支持行级 TTL 和列级 TTL

**对应的 .NET 代码**：

```csharp
public async Task CreateTableWithTTLAsync()
{
    using var connection = new ClickHouseConnection(_connectionString);
    await connection.OpenAsync();

    var command = connection.CreateCommand();
    command.CommandText = @"
        CREATE TABLE IF NOT EXISTS api_logs (
            timestamp DateTime,
            api_path String,
            status_code UInt16,
            error_message String TTL timestamp + INTERVAL 7 DAY  -- 错误信息 7 天后删除
        ) ENGINE = MergeTree()
        PARTITION BY toYYYYMMDD(timestamp)
        ORDER BY timestamp
        TTL timestamp + INTERVAL 30 DAY";  -- 整行 30 天后删除

    await command.ExecuteNonQueryAsync();
}
```

### 关键点

1. **分区键选择**：通常按时间分区（天、月）
2. **分区不宜过多**：每个分区至少 1 GB 数据
3. **TTL 自动执行**：后台定期检查并删除过期数据
4. **列级 TTL**：可以只删除某些列的数据

### 验证

测试分区删除性能：

```sql
-- 删除 1 天的数据（1 亿行）
ALTER TABLE api_logs DROP PARTITION '20240101';
-- 删除时间：< 1 秒
```

---

## 11. 物化视图

### 业务场景

你需要实时统计数据：
- 每分钟的 API 请求数
- 每小时的订单金额
- 每天的活跃用户数

用普通查询：
- 每次查询都要聚合原始数据
- 查询慢（秒级）
- 无法实时更新

### 问题

实时聚合查询很慢，需要预计算。

### 方案

使用物化视图预计算聚合结果。

### 实现

**步骤 1：创建物化视图**

```sql
-- 原始表
CREATE TABLE api_logs (
    timestamp DateTime,
    api_path String,
    status_code UInt16,
    response_time_ms UInt32
) ENGINE = MergeTree()
PARTITION BY toYYYYMMDD(timestamp)
ORDER BY timestamp;

-- 物化视图：每分钟的统计
CREATE MATERIALIZED VIEW api_stats_mv
ENGINE = SummingMergeTree()
PARTITION BY toYYYYMMDD(minute)
ORDER BY (minute, api_path)
AS SELECT
    toStartOfMinute(timestamp) AS minute,
    api_path,
    count() AS request_count,
    sum(response_time_ms) AS total_response_time,
    countIf(status_code >= 500) AS error_count
FROM api_logs
GROUP BY minute, api_path;
```

**原理**：
- 数据写入 `api_logs` 时，自动触发物化视图的聚合逻辑，结果写入 `api_stats_mv`
- 查询 `api_stats_mv` 比查询 `api_logs` 快 100 倍
- 使用 `SummingMergeTree` 引擎：后台合并时自动把相同 ORDER BY 键的行合并（数值列求和）。但合并是异步的——查询时可能存在还没合并的行，所以**查询时必须用 sum() 聚合**，不能直接读取 request_count 列

**步骤 2：查询物化视图**

```sql
-- 查询过去 1 小时每分钟的请求数
SELECT
    minute,
    sum(request_count) AS total_requests,
    sum(total_response_time) / sum(request_count) AS avg_response_time,
    sum(error_count) AS total_errors
FROM api_stats_mv
WHERE minute >= now() - INTERVAL 1 HOUR
GROUP BY minute
ORDER BY minute;
-- 查询时间：< 10ms（比查询原始表快 100 倍）
```

**对应的 .NET 代码**：

```csharp
public class MaterializedViewService
{
    private readonly string _connectionString;

    public MaterializedViewService(string connectionString)
    {
        _connectionString = connectionString;
    }

    // 创建物化视图
    public async Task CreateMaterializedViewAsync()
    {
        using var connection = new ClickHouseConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE MATERIALIZED VIEW IF NOT EXISTS api_stats_mv
            ENGINE = SummingMergeTree()
            PARTITION BY toYYYYMMDD(minute)
            ORDER BY (minute, api_path)
            AS SELECT
                toStartOfMinute(timestamp) AS minute,
                api_path,
                count() AS request_count,
                sum(response_time_ms) AS total_response_time,
                countIf(status_code >= 500) AS error_count
            FROM api_logs
            GROUP BY minute, api_path";

        await command.ExecuteNonQueryAsync();
    }

    // 查询物化视图
    public async Task<List<ApiMinuteStats>> GetMinuteStatsAsync(DateTime startTime)
    {
        using var connection = new ClickHouseConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT
                minute,
                api_path,
                sum(request_count) AS total_requests,
                sum(total_response_time) / sum(request_count) AS avg_response_time,
                sum(error_count) AS total_errors
            FROM api_stats_mv
            WHERE minute >= {startTime:DateTime}
            GROUP BY minute, api_path
            ORDER BY minute DESC, total_requests DESC
            LIMIT 100";

        command.Parameters.AddWithValue("startTime", startTime);

        var stats = new List<ApiMinuteStats>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            stats.Add(new ApiMinuteStats
            {
                Minute = reader.GetDateTime(0),
                ApiPath = reader.GetString(1),
                TotalRequests = reader.GetInt64(2),
                AvgResponseTime = reader.GetDouble(3),
                TotalErrors = reader.GetInt64(4)
            });
        }
        return stats;
    }
}

public class ApiMinuteStats
{
    public DateTime Minute { get; set; }
    public string ApiPath { get; set; }
    public long TotalRequests { get; set; }
    public double AvgResponseTime { get; set; }
    public long TotalErrors { get; set; }
}
```

### 关键点

1. **物化视图自动更新**：数据写入时触发
2. **使用 SummingMergeTree**：自动合并聚合结果
3. **物化视图占用空间**：需要额外存储空间
4. **不支持 UPDATE/DELETE**：只能追加数据

### 验证

对比查询性能：

```sql
-- 查询原始表
SELECT
    toStartOfMinute(timestamp) AS minute,
    count() AS request_count
FROM api_logs
WHERE timestamp >= now() - INTERVAL 1 HOUR
GROUP BY minute;
-- 查询时间：5000ms

-- 查询物化视图
SELECT
    minute,
    sum(request_count) AS request_count
FROM api_stats_mv
WHERE minute >= now() - INTERVAL 1 HOUR
GROUP BY minute;
-- 查询时间：10ms（快 500 倍）
```

---

[下一章：Part 4 - 集群部署](./ClickHouse教程-Part4-集群部署.md)
