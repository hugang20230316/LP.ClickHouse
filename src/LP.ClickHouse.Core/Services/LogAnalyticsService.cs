using ClickHouse.Driver;
using LP.ClickHouse.Core.Builders;
using LP.ClickHouse.Core.Entities;
using LP.ClickHouse.Core.Options;
using Microsoft.Extensions.Options;

namespace LP.ClickHouse.Core.Services;

/// <summary>
/// 执行示例项目需要的 ClickHouse 分析查询，并把原始结果映射成 API 模型。
/// </summary>
public class LogAnalyticsService : ILogAnalyticsService
{
    private readonly ClickHouseClient _client;
    private readonly string _database;

    /// <summary>
    /// 使用共享客户端和配置初始化分析服务。
    /// </summary>
    /// <param name="client">已注册到容器中的 ClickHouse 客户端。</param>
    /// <param name="options">包含数据库名等连接配置的选项对象。</param>
    public LogAnalyticsService(ClickHouseClient client, IOptions<ClickHouseOptions> options)
    {
        _client = client;
        _database = SchemaSqlBuilder.SanitizeIdentifier(options.Value.Database);
    }

    /// <summary>
    /// 查询最近一段时间内的 5xx 日志，便于快速排查错误。
    /// </summary>
    /// <param name="hours">向前回看的小时数。</param>
    /// <param name="limit">最多返回的记录条数。</param>
    /// <param name="cancellationToken">用于取消当前异步操作的令牌。</param>
    /// <returns>返回错误日志集合。</returns>
    public async Task<IReadOnlyList<ApiLogRecord>> GetRecentErrorsAsync(int hours, int limit, CancellationToken cancellationToken = default)
    {
        var safeHours = Math.Clamp(hours, 1, 24 * 30);
        var safeLimit = Math.Clamp(limit, 1, 200);
        var sql = $@"
SELECT log_id, timestamp, level, api_path, method, status_code, response_time_ms, user_id, ip_address, error_message, trace_id
FROM {_database}.api_logs
WHERE timestamp >= now() - INTERVAL {safeHours} HOUR
  AND status_code >= 500
ORDER BY timestamp DESC
LIMIT {safeLimit}";

        var results = new List<ApiLogRecord>();
        using var reader = await _client.ExecuteReaderAsync(sql, null, null, cancellationToken);
        while (reader.Read())
        {
            results.Add(new ApiLogRecord
            {
                LogId = reader.GetGuid(0), Timestamp = reader.GetDateTime(1), Level = reader.GetString(2), ApiPath = reader.GetString(3), Method = reader.GetString(4),
                StatusCode = Convert.ToUInt16(reader.GetValue(5)), ResponseTimeMs = Convert.ToUInt32(reader.GetValue(6)), UserId = Convert.ToUInt64(reader.GetValue(7)),
                IpAddress = reader.GetString(8), ErrorMessage = reader.GetString(9), TraceId = reader.GetString(10)
            });
        }
        return results;
    }

    /// <summary>
    /// 按小时和接口维度聚合请求量、错误量以及延迟指标。
    /// </summary>
    /// <param name="hours">向前回看的小时数。</param>
    /// <param name="limit">最多返回的聚合结果条数。</param>
    /// <param name="cancellationToken">用于取消当前异步操作的令牌。</param>
    /// <returns>返回接口统计结果集合。</returns>
    public async Task<IReadOnlyList<ApiEndpointStats>> GetApiStatsAsync(int hours, int limit, CancellationToken cancellationToken = default)
    {
        var safeHours = Math.Clamp(hours, 1, 24 * 30);
        var safeLimit = Math.Clamp(limit, 1, 200);
        var sql = $@"
SELECT toStartOfHour(timestamp) AS bucket_start, api_path, count() AS request_count, countIf(status_code >= 500) AS error_count,
       round(avg(response_time_ms), 2) AS avg_response_time_ms, quantile(0.95)(response_time_ms) AS p95_response_time_ms
FROM {_database}.api_logs
WHERE timestamp >= now() - INTERVAL {safeHours} HOUR
GROUP BY bucket_start, api_path
ORDER BY bucket_start DESC, request_count DESC
LIMIT {safeLimit}";

        var results = new List<ApiEndpointStats>();
        using var reader = await _client.ExecuteReaderAsync(sql, null, null, cancellationToken);
        while (reader.Read())
        {
            results.Add(new ApiEndpointStats
            {
                BucketStart = reader.GetDateTime(0), ApiPath = reader.GetString(1), RequestCount = Convert.ToInt64(reader.GetValue(2)), ErrorCount = Convert.ToInt64(reader.GetValue(3)),
                AvgResponseTimeMs = Convert.ToDouble(reader.GetValue(4)), P95ResponseTimeMs = Convert.ToDouble(reader.GetValue(5))
            });
        }
        return results;
    }

    /// <summary>
    /// 统计指定天数范围内的日活用户趋势。
    /// </summary>
    /// <param name="days">向前回看的天数。</param>
    /// <param name="cancellationToken">用于取消当前异步操作的令牌。</param>
    /// <returns>返回每天的活跃用户统计。</returns>
    public async Task<IReadOnlyList<DailyActiveUserMetric>> GetDailyActiveUsersAsync(int days, CancellationToken cancellationToken = default)
    {
        var safeDays = Math.Clamp(days, 1, 90);
        var sql = $@"
SELECT toDate(event_time) AS activity_date, uniq(user_id) AS active_users
FROM {_database}.user_events
WHERE event_time >= today() - INTERVAL {safeDays} DAY
GROUP BY activity_date
ORDER BY activity_date";

        var results = new List<DailyActiveUserMetric>();
        using var reader = await _client.ExecuteReaderAsync(sql, null, null, cancellationToken);
        while (reader.Read())
        {
            results.Add(new DailyActiveUserMetric { ActivityDate = reader.GetDateTime(0).Date, ActiveUsers = Convert.ToInt64(reader.GetValue(1)) });
        }
        return results;
    }

    /// <summary>
    /// 统计指定时间窗口内的浏览、点击、完成漏斗。
    /// </summary>
    /// <param name="days">向前回看的天数。</param>
    /// <param name="cancellationToken">用于取消当前异步操作的令牌。</param>
    /// <returns>返回漏斗统计结果。</returns>
    public async Task<FunnelMetric> GetFunnelAsync(int days, CancellationToken cancellationToken = default)
    {
        var safeDays = Math.Clamp(days, 1, 90);
        var sql = $@"
SELECT countIf(event_type = 'viewed_market') AS viewed_count,
       countIf(event_type = 'clicked_trade') AS clicked_count,
       countIf(event_type = 'completed_trade') AS completed_count,
       round(if(countIf(event_type = 'viewed_market') = 0, 0, countIf(event_type = 'clicked_trade') * 100.0 / countIf(event_type = 'viewed_market')), 2) AS view_to_click_rate,
       round(if(countIf(event_type = 'clicked_trade') = 0, 0, countIf(event_type = 'completed_trade') * 100.0 / countIf(event_type = 'clicked_trade')), 2) AS click_to_completion_rate
FROM {_database}.user_events
WHERE event_time >= now() - INTERVAL {safeDays} DAY";

        using var reader = await _client.ExecuteReaderAsync(sql, null, null, cancellationToken);
        if (!reader.Read()) return new FunnelMetric();

        return new FunnelMetric
        {
            ViewedCount = Convert.ToInt64(reader.GetValue(0)), ClickedCount = Convert.ToInt64(reader.GetValue(1)), CompletedCount = Convert.ToInt64(reader.GetValue(2)),
            ViewToClickRate = Convert.ToDouble(reader.GetValue(3)), ClickToCompletionRate = Convert.ToDouble(reader.GetValue(4))
        };
    }
}
