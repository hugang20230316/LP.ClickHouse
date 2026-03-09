# ClickHouse 教程 - Part 2: 实战场景

[返回目录](./ClickHouse教程-目录.md) | [上一章：Part 1 - 基础入门](./ClickHouse教程-Part1-基础入门.md)

---

## 4. 日志分析系统

### 业务场景

你的 ASP.NET Core 应用每天产生 5 亿条日志，需要：
- 实时查询错误日志（按时间、接口、错误类型）
- 统计每个接口的 QPS 和响应时间
- 分析慢查询（响应时间 > 1 秒）

用 Elasticsearch 存储日志：
- 写入性能尚可，但聚合分析是短板——按接口 × 状态码 × 时间粒度做多维聚合，DSL 写到崩溃
- 存储成本高（倒排索引 + doc_values + _source，1 TB 日志实际占用 3-5 TB）
- 想做同比环比、窗口函数？ES 不支持，只能导出到应用层计算

### 问题

Elasticsearch 做日志搜索很强，但做日志分析力不从心：
- 日志是只写不改的数据，ES 的倒排索引维护成本被浪费了
- 查询模式固定（按时间范围 + 其他条件），不需要全文搜索能力
- 数据量大时聚合性能下降严重，高基数字段的 terms 聚合尤其慢

### 方案

使用 ClickHouse 存储日志，利用列式存储和压缩优势。

### 实现

**步骤 1：创建日志表**

`````sql
CREATE TABLE api_logs (
    log_id UUID DEFAULT generateUUIDv4(),
    timestamp DateTime64(3),
    level Enum8('Debug' = 1, 'Info' = 2, 'Warning' = 3, 'Error' = 4, 'Fatal' = 5),
    api_path String,
    method Enum8('GET' = 1, 'POST' = 2, 'PUT' = 3, 'DELETE' = 4),
    status_code UInt16,
    response_time_ms UInt32,
    user_id UInt64,
    ip_address IPv4,
    error_message String,
    stack_trace String
) ENGINE = MergeTree()
PARTITION BY toYYYYMMDD(timestamp)  -- 按天分区
ORDER BY (timestamp, api_path, level)
TTL timestamp + INTERVAL 30 DAY;  -- 保留 30 天
```

**步骤 2：.NET 日志写入**

```csharp
using ClickHouse.Client.ADO;
using ClickHouse.Client.Copy;

public class ClickHouseLogWriter
{
    private readonly string _connectionString;

    public ClickHouseLogWriter(string connectionString)
    {
        _connectionString = connectionString;
    }

    // 批量写入日志（高性能）
    public async Task WriteBatchAsync(List<ApiLog> logs)
    {
        using var connection = new ClickHouseConnection(_connectionString);
        await connection.OpenAsync();

        // 使用 BulkCopy 批量写入（比逐条 INSERT 快 100 倍）
        using var bulkCopy = new ClickHouseBulkCopy(connection)
        {
            DestinationTableName = "api_logs",
            BatchSize = 10000
        };

        var dataTable = ConvertToDataTable(logs);
        await bulkCopy.WriteToServerAsync(dataTable);
    }

    private DataTable ConvertToDataTable(List<ApiLog> logs)
    {
        var table = new DataTable();
        table.Columns.Add("log_id", typeof(Guid));
        table.Columns.Add("timestamp", typeof(DateTime));
        table.Columns.Add("level", typeof(byte));
        table.Columns.Add("api_path", typeof(string));
        table.Columns.Add("method", typeof(byte));
        table.Columns.Add("status_code", typeof(ushort));
        table.Columns.Add("response_time_ms", typeof(uint));
        table.Columns.Add("user_id", typeof(ulong));
        table.Columns.Add("ip_address", typeof(string));
        table.Columns.Add("error_message", typeof(string));
        table.Columns.Add("stack_trace", typeof(string));

        foreach (var log in logs)
        {
            table.Rows.Add(
                log.LogId,
                log.Timestamp,
                (byte)log.Level,
                log.ApiPath,
                (byte)log.Method,
                log.StatusCode,
                log.ResponseTimeMs,
                log.UserId,
                log.IpAddress,
                log.ErrorMessage ?? string.Empty,
                log.StackTrace ?? string.Empty
            );
        }

        return table;
    }
}

public class ApiLog
{
    public Guid LogId { get; set; } = Guid.NewGuid();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public LogLevel Level { get; set; }
    public string ApiPath { get; set; }
    public HttpMethod Method { get; set; }
    public ushort StatusCode { get; set; }
    public uint ResponseTimeMs { get; set; }
    public ulong UserId { get; set; }
    public string IpAddress { get; set; }
    public string ErrorMessage { get; set; }
    public string StackTrace { get; set; }
}

public enum LogLevel : byte
{
    Debug = 1,
    Info = 2,
    Warning = 3,
    Error = 4,
    Fatal = 5
}
```

**步骤 3：常见查询**

```csharp
public class LogQueryService
{
    private readonly string _connectionString;

    public LogQueryService(string connectionString)
    {
        _connectionString = connectionString;
    }

    // 查询 1：过去 1 小时的错误日志
    public async Task<List<ApiLog>> GetRecentErrorsAsync()
    {
        using var connection = new ClickHouseConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT
                log_id,
                timestamp,
                api_path,
                status_code,
                error_message
            FROM api_logs
            WHERE timestamp >= now() - INTERVAL 1 HOUR
              AND level >= 4  -- Error 或 Fatal
            ORDER BY timestamp DESC
            LIMIT 100";

        var logs = new List<ApiLog>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            logs.Add(new ApiLog
            {
                LogId = reader.GetGuid(0),
                Timestamp = reader.GetDateTime(1),
                ApiPath = reader.GetString(2),
                StatusCode = reader.GetFieldValue<ushort>(3),
                ErrorMessage = reader.GetString(4)
            });
        }
        return logs;
    }

    // 查询 2：每个接口的 QPS 和平均响应时间
    public async Task<List<ApiStats>> GetApiStatsAsync(DateTime startTime, DateTime endTime)
    {
        using var connection = new ClickHouseConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT
                api_path,
                count() AS request_count,
                count() / dateDiff('second', {startTime:DateTime64(3)}, {endTime:DateTime64(3)}) AS qps,
                avg(response_time_ms) AS avg_response_time,
                quantile(0.95)(response_time_ms) AS p95_response_time,
                countIf(status_code >= 500) AS error_count
            FROM api_logs
            WHERE timestamp BETWEEN {startTime:DateTime64(3)} AND {endTime:DateTime64(3)}
            GROUP BY api_path
            ORDER BY request_count DESC
            LIMIT 20";

        command.Parameters.AddWithValue("startTime", startTime);
        command.Parameters.AddWithValue("endTime", endTime);

        var stats = new List<ApiStats>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            stats.Add(new ApiStats
            {
                ApiPath = reader.GetString(0),
                RequestCount = reader.GetInt64(1),
                Qps = reader.GetDouble(2),
                AvgResponseTime = reader.GetDouble(3),
                P95ResponseTime = reader.GetDouble(4),
                ErrorCount = reader.GetInt64(5)
            });
        }
        return stats;
    }

    // 查询 3：慢查询分析
    public async Task<List<ApiLog>> GetSlowQueriesAsync(uint thresholdMs = 1000)
    {
        using var connection = new ClickHouseConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT
                timestamp,
                api_path,
                method,
                response_time_ms,
                user_id
            FROM api_logs
            WHERE timestamp >= now() - INTERVAL 1 HOUR
              AND response_time_ms > {thresholdMs:UInt32}
            ORDER BY response_time_ms DESC
            LIMIT 100";

        command.Parameters.AddWithValue("thresholdMs", thresholdMs);

        var logs = new List<ApiLog>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            logs.Add(new ApiLog
            {
                Timestamp = reader.GetDateTime(0),
                ApiPath = reader.GetString(1),
                Method = (HttpMethod)reader.GetByte(2),
                ResponseTimeMs = reader.GetFieldValue<uint>(3),
                UserId = reader.GetFieldValue<ulong>(4)
            });
        }
        return logs;
    }
}

public class ApiStats
{
    public string ApiPath { get; set; }
    public long RequestCount { get; set; }
    public double Qps { get; set; }
    public double AvgResponseTime { get; set; }
    public double P95ResponseTime { get; set; }
    public long ErrorCount { get; set; }
}
```

### 关键点

1. **使用 BulkCopy 批量写入**：比逐条 INSERT 快 100 倍
2. **按天分区**：方便删除过期数据，避免全表扫描
3. **排序键设计**：`(timestamp, api_path, level)` 适合按时间范围查询
4. **使用 quantile 函数**：计算 P95、P99 响应时间

### 验证

测试写入性能：

```csharp
// 写入 100 万条日志
var logs = Enumerable.Range(0, 1000000)
    .Select(i => new ApiLog
    {
        Timestamp = DateTime.UtcNow.AddSeconds(-i),
        Level = LogLevel.Info,
        ApiPath = $"/api/test/{i % 100}",
        Method = HttpMethod.GET,
        StatusCode = 200,
        ResponseTimeMs = (uint)(i % 1000),
        UserId = (ulong)(i % 10000),
        IpAddress = "192.168.1.1"
    })
    .ToList();

var stopwatch = Stopwatch.StartNew();
await logWriter.WriteBatchAsync(logs);
stopwatch.Stop();

Console.WriteLine($"写入 100 万条日志耗时: {stopwatch.ElapsedMilliseconds}ms");
// 预期：< 5 秒
```

---

## 5. 用户行为分析

### 业务场景

你的电商网站需要分析用户行为：
- 统计每天的活跃用户数（DAU）
- 分析用户留存率（次日留存、7 日留存）
- 计算用户漏斗转化率（浏览 → 加购 → 下单 → 支付）

用 Elasticsearch 分析：
- 数据量大（每天 1 亿条事件），高基数聚合（按 user_id 去重）性能差
- 漏斗分析需要多步骤有序匹配，ES 的 DSL 几乎无法表达
- 留存率计算需要窗口函数和自 JOIN，ES 完全不支持

### 问题

Elasticsearch 不适合行为分析：
- 不支持 JOIN 和子查询，无法关联多个事件序列
- 高基数去重（cardinality 聚合）只是近似值，且数据量大时很慢
- 没有漏斗函数，无法原生计算转化率

### 方案

使用 ClickHouse 的聚合函数和窗口函数。

### 实现

**步骤 1：创建事件表**

`````sql
CREATE TABLE user_events (
    event_id UUID DEFAULT generateUUIDv4(),
    user_id UInt64,
    event_time DateTime64(3),
    event_type Enum8(
        'page_view' = 1,
        'add_to_cart' = 2,
        'create_order' = 3,
        'payment' = 4
    ),
    page_url String,
    product_id UInt64,
    order_id UInt64,
    amount Decimal(18, 2),
    properties String  -- JSON 格式的额外属性
) ENGINE = MergeTree()
PARTITION BY toYYYYMMDD(event_time)
ORDER BY (user_id, event_time);
```

**步骤 2：.NET 事件写入**

```csharp
public class UserEventTracker
{
    private readonly string _connectionString;

    public UserEventTracker(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task TrackEventAsync(UserEvent userEvent)
    {
        using var connection = new ClickHouseConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO user_events (
                user_id, event_time, event_type, page_url,
                product_id, order_id, amount, properties
            ) VALUES (
                {userId:UInt64},
                {eventTime:DateTime64(3)},
                {eventType:UInt8},
                {pageUrl:String},
                {productId:UInt64},
                {orderId:UInt64},
                {amount:Decimal(18,2)},
                {properties:String}
            )";

        command.Parameters.AddWithValue("userId", userEvent.UserId);
        command.Parameters.AddWithValue("eventTime", userEvent.EventTime);
        command.Parameters.AddWithValue("eventType", (byte)userEvent.EventType);
        command.Parameters.AddWithValue("pageUrl", userEvent.PageUrl ?? string.Empty);
        command.Parameters.AddWithValue("productId", userEvent.ProductId);
        command.Parameters.AddWithValue("orderId", userEvent.OrderId);
        command.Parameters.AddWithValue("amount", userEvent.Amount);
        command.Parameters.AddWithValue("properties", userEvent.Properties ?? "{}");

        await command.ExecuteNonQueryAsync();
    }
}

public class UserEvent
{
    public ulong UserId { get; set; }
    public DateTime EventTime { get; set; } = DateTime.UtcNow;
    public EventType EventType { get; set; }
    public string PageUrl { get; set; }
    public ulong ProductId { get; set; }
    public ulong OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Properties { get; set; }
}

public enum EventType : byte
{
    PageView = 1,
    AddToCart = 2,
    CreateOrder = 3,
    Payment = 4
}
```

**步骤 3：常见分析查询**

```csharp
public class UserAnalyticsService
{
    private readonly string _connectionString;

    public UserAnalyticsService(string connectionString)
    {
        _connectionString = connectionString;
    }

    // 分析 1：每天的活跃用户数（DAU）
    public async Task<List<DauStats>> GetDauAsync(DateTime startDate, DateTime endDate)
    {
        using var connection = new ClickHouseConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT
                toDate(event_time) AS date,
                uniq(user_id) AS dau
            FROM user_events
            WHERE event_time BETWEEN {startDate:DateTime} AND {endDate:DateTime}
            GROUP BY date
            ORDER BY date";

        command.Parameters.AddWithValue("startDate", startDate);
        command.Parameters.AddWithValue("endDate", endDate);

        var stats = new List<DauStats>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            stats.Add(new DauStats
            {
                Date = reader.GetDateTime(0),
                Dau = reader.GetInt64(1)
            });
        }
        return stats;
    }

    // 分析 2：用户留存率
    public async Task<List<RetentionStats>> GetRetentionAsync(DateTime cohortDate)
    {
        using var connection = new ClickHouseConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = @"
            WITH first_day_users AS (
                SELECT DISTINCT user_id
                FROM user_events
                WHERE toDate(event_time) = {cohortDate:Date}
            )
            SELECT
                day_offset,
                uniq(user_id) AS retained_users,
                retained_users / (SELECT count() FROM first_day_users) AS retention_rate
            FROM (
                SELECT
                    user_id,
                    dateDiff('day', {cohortDate:Date}, toDate(event_time)) AS day_offset
                FROM user_events
                WHERE user_id IN (SELECT user_id FROM first_day_users)
                  AND toDate(event_time) >= {cohortDate:Date}
            )
            GROUP BY day_offset
            ORDER BY day_offset
            LIMIT 30";

        command.Parameters.AddWithValue("cohortDate", cohortDate);

        var stats = new List<RetentionStats>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            stats.Add(new RetentionStats
            {
                DayOffset = reader.GetInt32(0),
                RetainedUsers = reader.GetInt64(1),
                RetentionRate = reader.GetDouble(2)
            });
        }
        return stats;
    }

    // 分析 3：漏斗转化率（简化版：独立统计每步用户数）
    public async Task<FunnelStats> GetFunnelAsync(DateTime startTime, DateTime endTime)
    {
        using var connection = new ClickHouseConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        // 注意：这里用 uniqExactIf 独立统计每个步骤的用户数，
        // 没有强制事件顺序（即用户不一定按 浏览→加购→下单→支付 的顺序）。
        // 如果需要严格的有序漏斗（用户必须先浏览再加购再下单），
        // 应使用 ClickHouse 的 windowFunnel() 函数，见下方补充示例。
        command.CommandText = @"
            SELECT
                uniqExact(user_id) AS total_users,
                uniqExactIf(user_id, event_type = 1) AS page_view_users,
                uniqExactIf(user_id, event_type = 2) AS add_to_cart_users,
                uniqExactIf(user_id, event_type = 3) AS create_order_users,
                uniqExactIf(user_id, event_type = 4) AS payment_users
            FROM user_events
            WHERE event_time BETWEEN {startTime:DateTime64(3)} AND {endTime:DateTime64(3)}";

        command.Parameters.AddWithValue("startTime", startTime);
        command.Parameters.AddWithValue("endTime", endTime);

        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            var totalUsers = reader.GetInt64(0);
            var pageViewUsers = reader.GetInt64(1);
            var addToCartUsers = reader.GetInt64(2);
            var createOrderUsers = reader.GetInt64(3);
            var paymentUsers = reader.GetInt64(4);

            return new FunnelStats
            {
                TotalUsers = totalUsers,
                PageViewUsers = pageViewUsers,
                AddToCartUsers = addToCartUsers,
                CreateOrderUsers = createOrderUsers,
                PaymentUsers = paymentUsers,
                // 防止除零
                AddToCartRate = pageViewUsers > 0 ? (double)addToCartUsers / pageViewUsers : 0,
                CreateOrderRate = addToCartUsers > 0 ? (double)createOrderUsers / addToCartUsers : 0,
                PaymentRate = createOrderUsers > 0 ? (double)paymentUsers / createOrderUsers : 0
            };
        }

        return null;
    }
}

public class DauStats
{
    public DateTime Date { get; set; }
    public long Dau { get; set; }
}

public class RetentionStats
{
    public int DayOffset { get; set; }
    public long RetainedUsers { get; set; }
    public double RetentionRate { get; set; }
}

public class FunnelStats
{
    public long TotalUsers { get; set; }
    public long PageViewUsers { get; set; }
    public long AddToCartUsers { get; set; }
    public long CreateOrderUsers { get; set; }
    public long PaymentUsers { get; set; }
    public double AddToCartRate { get; set; }
    public double CreateOrderRate { get; set; }
    public double PaymentRate { get; set; }
}
```

### 关键点

1. **使用 uniq() 函数**：基于 HyperLogLog 算法的近似去重，比 COUNT(DISTINCT) 快 10 倍，误差约 1%。需要精确值时用 `uniqExact()`
2. **使用 uniqExactIf() 函数**：条件去重，适合简化版漏斗分析
3. **使用 CTE（WITH 子句）**：简化复杂查询
4. **排序键设计**：`(user_id, event_time)` 适合按用户查询

### 补充：严格有序漏斗（windowFunnel）

上面的 `uniqExactIf` 方案只是独立统计每步用户数，不保证事件顺序。如果需要严格的有序漏斗——用户必须按"浏览 → 加购 → 下单 → 支付"的顺序依次完成，用 `windowFunnel()`：

`````sql
-- windowFunnel(window)(timestamp, cond1, cond2, cond3, ...)
-- window：事件之间的最大时间间隔（秒）
-- 返回值：用户在窗口期内最多完成到第几步

SELECT
    level,
    count() AS user_count
FROM (
    SELECT
        user_id,
        windowFunnel(86400)(                    -- 24 小时窗口期
            event_time,
            event_type = 1,                     -- 第 1 步：浏览
            event_type = 2,                     -- 第 2 步：加购
            event_type = 3,                     -- 第 3 步：下单
            event_type = 4                      -- 第 4 步：支付
        ) AS level
    FROM user_events
    WHERE event_time BETWEEN '2026-03-01' AND '2026-03-07'
    GROUP BY user_id
)
GROUP BY level
ORDER BY level;
-- 输出示例：
-- level 1: 50000（浏览了但没加购）
-- level 2: 12000（加购了但没下单）
-- level 3: 8000 （下单了但没支付）
-- level 4: 6000 （完成支付）
```

`windowFunnel` 保证事件严格有序：用户必须先触发 cond1，再在窗口期内触发 cond2，以此类推。这才是真正的漏斗分析。

### 验证

测试查询性能：

`````sql
-- 查询过去 30 天的 DAU（1 亿条事件）
SELECT
    toDate(event_time) AS date,
    uniq(user_id) AS dau
FROM user_events
WHERE event_time >= now() - INTERVAL 30 DAY
GROUP BY date
ORDER BY date;
-- 预期查询时间：< 500ms
```

---

## 6. 准 CRUD 场景：订单快照

### 业务场景

电商订单每天会经历多次状态变化：待支付 → 已支付 → 已发货 → 已签收。业务既要看“当前最新状态”，又要按城市、时间、状态做聚合分析。

### 问题

ClickHouse 适合高吞吐写入和大范围分析，不适合 MySQL 那种“按主键频繁 UPDATE 单行”的事务型 CRUD。真实项目里应该设计成 **准 CRUD**：
- **Create**：新增一条快照
- **Read**：读最新快照或历史版本
- **Update**：写入更高版本的新快照
- **Delete**：优先逻辑删除，物理删除只做低频治理

### 方案

使用 `ReplacingMergeTree(version)` 保存订单快照。每次订单状态变化，不改旧数据，而是写一条新版本。

### 实现

```sql
CREATE TABLE order_snapshots
(
    order_id UInt64,
    user_id UInt64,
    order_no String,
    status LowCardinality(String),
    pay_amount Decimal(18, 2),
    city LowCardinality(String),
    created_at DateTime64(3, 'UTC'),
    updated_at DateTime64(3, 'UTC'),
    version UInt64,
    is_deleted UInt8 DEFAULT 0
)
ENGINE = ReplacingMergeTree(version)
PARTITION BY toYYYYMM(created_at)
ORDER BY (order_id, updated_at)
TTL created_at + INTERVAL 180 DAY;

-- Create：新建订单
INSERT INTO order_snapshots VALUES
(202603090001, 10086, 'NO202603090001', 'PendingPayment', 199.00, '上海', now64(3), now64(3), 1, 0);

-- Update：不要 UPDATE 原行，而是补一条更高版本的新快照
INSERT INTO order_snapshots VALUES
(202603090001, 10086, 'NO202603090001', 'Paid', 199.00, '上海', now64(3), now64(3), 2, 0);

-- Read：查订单最新状态，argMax 表示“取 version 最大那行对应的值”
SELECT order_id, argMax(status, version) AS latest_status, max(updated_at) AS latest_updated_at
FROM order_snapshots
WHERE order_id = 202603090001 AND is_deleted = 0
GROUP BY order_id;

-- Read：查历史版本
SELECT order_id, status, version, updated_at
FROM order_snapshots
WHERE order_id = 202603090001
ORDER BY version;

-- Delete：逻辑删除优先，物理删除只做低频清理
INSERT INTO order_snapshots VALUES
(202603090001, 10086, 'NO202603090001', 'Paid', 199.00, '上海', now64(3), now64(3), 3, 1);

ALTER TABLE order_snapshots DELETE WHERE order_id = 202603090001;
```

### 关键点

1. **ClickHouse 的 CRUD 是业务语义 CRUD**，不是行级事务 CRUD
2. **高频更新用版本快照**，不要把 mutation 当常规写路径
3. **查最新值优先用 `argMax`**，比很多子查询写法更直接
4. **删除优先逻辑删除**，既保留审计能力，也避免频繁重写分片

### 验证

```sql
SELECT order_id, argMax(status, version) AS latest_status, max(version) AS latest_version
FROM order_snapshots
WHERE order_id = 202603090001
GROUP BY order_id;

-- 预期结果：latest_status = 'Paid'，latest_version = 3（其中 version=3 是删除标记版本）
```

---

## 7. 复杂查询：多维聚合、留存和路径分析

### 业务场景

运营团队不会只问“今天多少用户活跃”，而是会连续追问：
- 最近 7 天，哪个接口在 **高流量 + 高错误率** 的情况下最慢？
- 新用户在第 1、3、7 天的留存分别是多少？
- 用户到底是“浏览了没点击”，还是“点击了但没完成交易”？

### 问题

简单 `GROUP BY` 只能回答“总数”，很难直接回答多维分析、留存和路径问题。把这些计算搬到应用层，又会变成大量内存聚合和中间对象处理。

### 方案

把复杂查询拆成 3 类常见模型：
- **多维聚合**：找慢接口、热点页面、异常设备
- **Cohort 留存**：看同一批用户后续有没有回来
- **路径分析**：看用户卡在哪一步

### 实现

```sql
-- 查询 1：多维聚合，定位“慢且错得多”的接口
SELECT
    toStartOfHour(timestamp) AS hour_bucket,
    api_path,
    method,
    count() AS request_count,
    countIf(status_code >= 500) AS error_count,
    round(error_count * 100.0 / request_count, 2) AS error_rate,
    quantile(0.95)(response_time_ms) AS p95_rt
FROM api_logs
WHERE timestamp >= now() - INTERVAL 7 DAY
GROUP BY hour_bucket, api_path, method
HAVING request_count >= 1000 AND error_rate >= 1
ORDER BY p95_rt DESC, error_rate DESC
LIMIT 20;

-- 查询 2：Cohort 留存，观察 D1 / D3 / D7
WITH first_visit AS
(
    SELECT user_id, min(toDate(event_time)) AS cohort_date
    FROM user_events
    WHERE event_time >= today() - INTERVAL 14 DAY
    GROUP BY user_id
)
SELECT
    cohort_date,
    dateDiff('day', cohort_date, toDate(user_events.event_time)) AS day_offset,
    uniq(user_events.user_id) AS retained_users
FROM user_events
INNER JOIN first_visit ON user_events.user_id = first_visit.user_id
WHERE day_offset IN (0, 1, 3, 7)
GROUP BY cohort_date, day_offset
ORDER BY cohort_date, day_offset;

-- 查询 3：路径分析，用户卡在哪一步
SELECT level, count() AS user_count
FROM
(
    SELECT
        user_id,
        windowFunnel(3600)(
            event_time,
            event_type = 'viewed_market',
            event_type = 'clicked_trade',
            event_type = 'completed_trade'
        ) AS level
    FROM user_events
    WHERE event_time >= now() - INTERVAL 7 DAY
    GROUP BY user_id
)
GROUP BY level
ORDER BY level;
```

### 关键点

1. **复杂查询优先留在 SQL 层**，让 ClickHouse 扫列和聚合
2. **多维聚合要先设筛选门槛**，否则榜单会被低流量噪音污染
3. **留存的关键是 cohort**，不是简单看某天活跃数
4. **路径分析要看顺序**，`windowFunnel` 比单独 `countIf` 更贴近真实业务

### 验证

```sql
-- 验证慢接口榜单是否可信：不能全是低流量接口
SELECT api_path, count() AS request_count, quantile(0.95)(response_time_ms) AS p95_rt
FROM api_logs
WHERE timestamp >= now() - INTERVAL 1 DAY
GROUP BY api_path
HAVING request_count >= 1000
ORDER BY p95_rt DESC
LIMIT 10;

-- 验证漏斗是否按顺序统计
SELECT user_id, groupArray(event_type ORDER BY event_time) AS event_path
FROM user_events
WHERE event_time >= now() - INTERVAL 1 DAY
GROUP BY user_id
LIMIT 5;
```

---

[下一章：Part 3 - 性能优化](./ClickHouse教程-Part3-性能优化.md)


