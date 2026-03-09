using ClickHouse.Driver;
using LP.ClickHouse.Core.Builders;
using LP.ClickHouse.Core.Entities;
using LP.ClickHouse.Core.Options;
using Microsoft.Extensions.Options;

namespace LP.ClickHouse.Core.Services;

/// <summary>
/// 封装教程中的多维聚合、分群留存和路径漏斗查询。
/// </summary>
public class AdvancedAnalyticsService : IAdvancedAnalyticsService
{
    private readonly ClickHouseClient _client;
    private readonly string _database;

    /// <summary>
    /// 使用共享客户端和配置初始化复杂分析服务。
    /// </summary>
    /// <param name="client">已注册到容器中的 ClickHouse 客户端。</param>
    /// <param name="options">包含数据库名等连接配置的选项对象。</param>
    public AdvancedAnalyticsService(ClickHouseClient client, IOptions<ClickHouseOptions> options)
    {
        _client = client;
        _database = SchemaSqlBuilder.SanitizeIdentifier(options.Value.Database);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SlowApiMetric>> GetSlowApisAsync(int days, int limit, int minRequestCount, double minErrorRate, CancellationToken cancellationToken = default)
    {
        var safeDays = Math.Clamp(days, 1, 30);
        var safeLimit = Math.Clamp(limit, 1, 100);
        var safeMinRequestCount = Math.Clamp(minRequestCount, 1, 200000);
        var safeMinErrorRate = Math.Clamp(minErrorRate, 0, 100);
        var sql = $@"
SELECT
    api_path,
    method,
    count() AS request_count,
    countIf(status_code >= 500) AS error_count,
    round(if(count() = 0, 0, countIf(status_code >= 500) * 100.0 / count()), 2) AS error_rate,
    round(avg(response_time_ms), 2) AS avg_response_time_ms,
    quantile(0.95)(response_time_ms) AS p95_response_time_ms
FROM {_database}.api_logs
WHERE timestamp >= now() - INTERVAL {safeDays} DAY
GROUP BY api_path, method
HAVING count() >= {safeMinRequestCount}
   AND round(if(count() = 0, 0, countIf(status_code >= 500) * 100.0 / count()), 2) >= {safeMinErrorRate}
ORDER BY p95_response_time_ms DESC, error_rate DESC
LIMIT {safeLimit}";

        var results = new List<SlowApiMetric>();
        using var reader = await _client.ExecuteReaderAsync(sql, null, null, cancellationToken);
        while (reader.Read())
        {
            results.Add(new SlowApiMetric
            {
                ApiPath = reader.GetString(0),
                Method = reader.GetString(1),
                RequestCount = Convert.ToInt64(reader.GetValue(2)),
                ErrorCount = Convert.ToInt64(reader.GetValue(3)),
                ErrorRate = Convert.ToDouble(reader.GetValue(4)),
                AvgResponseTimeMs = Convert.ToDouble(reader.GetValue(5)),
                P95ResponseTimeMs = Convert.ToDouble(reader.GetValue(6))
            });
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RetentionMetric>> GetRetentionAsync(int lookbackDays, CancellationToken cancellationToken = default)
    {
        var safeLookbackDays = Math.Clamp(lookbackDays, 1, 60);
        var sql = $@"
WITH first_visit AS
(
    SELECT
        user_id,
        min(toDate(event_time)) AS cohort_date
    FROM {_database}.user_events
    WHERE event_time >= today() - INTERVAL {safeLookbackDays} DAY
    GROUP BY user_id
),
cohort_size AS
(
    SELECT cohort_date, count() AS cohort_users
    FROM first_visit
    GROUP BY cohort_date
)
SELECT
    first_visit.cohort_date,
    dateDiff('day', first_visit.cohort_date, toDate(user_events.event_time)) AS day_offset,
    uniq(user_events.user_id) AS retained_users,
    round(uniq(user_events.user_id) * 100.0 / cohort_size.cohort_users, 2) AS retention_rate
FROM {_database}.user_events AS user_events
INNER JOIN first_visit ON user_events.user_id = first_visit.user_id
INNER JOIN cohort_size ON first_visit.cohort_date = cohort_size.cohort_date
WHERE dateDiff('day', first_visit.cohort_date, toDate(user_events.event_time)) IN (0, 1, 3, 7)
GROUP BY first_visit.cohort_date, day_offset, cohort_size.cohort_users
ORDER BY first_visit.cohort_date DESC, day_offset ASC";

        var results = new List<RetentionMetric>();
        using var reader = await _client.ExecuteReaderAsync(sql, null, null, cancellationToken);
        while (reader.Read())
        {
            results.Add(new RetentionMetric
            {
                CohortDate = reader.GetDateTime(0).Date,
                DayOffset = Convert.ToInt32(reader.GetValue(1)),
                RetainedUsers = Convert.ToInt64(reader.GetValue(2)),
                RetentionRate = Convert.ToDouble(reader.GetValue(3))
            });
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PathFunnelMetric>> GetPathFunnelAsync(int days, int windowSeconds, CancellationToken cancellationToken = default)
    {
        var safeDays = Math.Clamp(days, 1, 30);
        var safeWindowSeconds = Math.Clamp(windowSeconds, 60, 86400);
        var sql = $@"
SELECT level, count() AS user_count
FROM
(
    SELECT
        user_id,
        windowFunnel({safeWindowSeconds})(
            event_time,
            event_type = 'viewed_market',
            event_type = 'clicked_trade',
            event_type = 'completed_trade'
        ) AS level
    FROM {_database}.user_events
    WHERE event_time >= now() - INTERVAL {safeDays} DAY
    GROUP BY user_id
)
GROUP BY level
ORDER BY level";

        var results = new List<PathFunnelMetric>();
        using var reader = await _client.ExecuteReaderAsync(sql, null, null, cancellationToken);
        while (reader.Read())
        {
            var level = Convert.ToInt32(reader.GetValue(0));
            results.Add(new PathFunnelMetric
            {
                Level = level,
                StepName = ResolveStepName(level),
                UserCount = Convert.ToInt64(reader.GetValue(1))
            });
        }

        return results;
    }

    private static string ResolveStepName(int level)
    {
        return level switch
        {
            <= 0 => "未进入漏斗",
            1 => "浏览市场",
            2 => "点击交易",
            3 => "完成交易",
            _ => $"完成到第 {level} 步"
        };
    }
}
