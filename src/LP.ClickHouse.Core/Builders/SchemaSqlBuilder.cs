using System.Text.RegularExpressions;

namespace LP.ClickHouse.Core.Builders;

/// <summary>
/// 集中维护建库建表相关 SQL，避免初始化逻辑和测试逻辑出现不一致。
/// </summary>
public static class SchemaSqlBuilder
{
    private static readonly Regex IdentifierRegex = new("^[A-Za-z0-9_]+$", RegexOptions.Compiled);

    /// <summary>
    /// 校验数据库名或表名等标识符，防止非法字符进入 SQL。
    /// </summary>
    /// <param name="identifier">需要校验的标识符文本。</param>
    /// <returns>校验通过后返回原始标识符。</returns>
    public static string SanitizeIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier) || !IdentifierRegex.IsMatch(identifier))
        {
            throw new ArgumentException("标识符只能包含字母、数字和下划线", nameof(identifier));
        }

        return identifier;
    }

    /// <summary>
    /// 生成创建数据库的 SQL。
    /// </summary>
    /// <param name="database">目标数据库名。</param>
    /// <returns>可直接执行的建库 SQL。</returns>
    public static string BuildCreateDatabaseSql(string database)
    {
        var safeDatabase = SanitizeIdentifier(database);
        return $"CREATE DATABASE IF NOT EXISTS {safeDatabase}";
    }

    /// <summary>
    /// 生成 API 日志表示例的建表 SQL，按月分区并保留 30 天数据。
    /// </summary>
    /// <param name="database">目标数据库名。</param>
    /// <returns>可直接执行的建表 SQL。</returns>
    public static string BuildCreateApiLogsTableSql(string database)
    {
        var safeDatabase = SanitizeIdentifier(database);
        return $@"
CREATE TABLE IF NOT EXISTS {safeDatabase}.api_logs
(
    log_id UUID,
    timestamp DateTime64(3, 'UTC'),
    level LowCardinality(String),
    api_path LowCardinality(String),
    method LowCardinality(String),
    status_code UInt16,
    response_time_ms UInt32,
    user_id UInt64,
    ip_address String,
    error_message String,
    trace_id String
)
ENGINE = MergeTree()
PARTITION BY toYYYYMM(timestamp)
ORDER BY (timestamp, api_path, status_code)
TTL timestamp + INTERVAL 30 DAY
SETTINGS index_granularity = 8192;".Trim();
    }

    /// <summary>
    /// 生成用户行为表示例的建表 SQL，供 DAU 和漏斗分析使用。
    /// </summary>
    /// <param name="database">目标数据库名。</param>
    /// <returns>可直接执行的建表 SQL。</returns>
    public static string BuildCreateUserEventsTableSql(string database)
    {
        var safeDatabase = SanitizeIdentifier(database);
        return $@"
CREATE TABLE IF NOT EXISTS {safeDatabase}.user_events
(
    event_time DateTime64(3, 'UTC'),
    user_id UInt64,
    session_id String,
    event_type LowCardinality(String),
    page LowCardinality(String),
    device LowCardinality(String),
    trace_id String
)
ENGINE = MergeTree()
PARTITION BY toYYYYMM(event_time)
ORDER BY (event_time, event_type, user_id)
TTL event_time + INTERVAL 90 DAY
SETTINGS index_granularity = 8192;".Trim();
    }

    /// <summary>
    /// 生成订单快照表示例的建表 SQL，供准 CRUD 和复杂查询教学场景使用。
    /// </summary>
    /// <param name="database">目标数据库名。</param>
    /// <returns>可直接执行的建表 SQL。</returns>
    public static string BuildCreateOrderSnapshotsTableSql(string database)
    {
        var safeDatabase = SanitizeIdentifier(database);
        return $@"
CREATE TABLE IF NOT EXISTS {safeDatabase}.order_snapshots
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
TTL created_at + INTERVAL 180 DAY
SETTINGS index_granularity = 8192;".Trim();
    }
}
