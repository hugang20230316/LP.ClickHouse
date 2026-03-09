using LP.ClickHouse.Core.Entities;

namespace LP.ClickHouse.Core.Services;

/// <summary>
/// 定义示例 API 暴露的 ClickHouse 分析查询能力。
/// </summary>
public interface ILogAnalyticsService
{
    /// <summary>
    /// 查询最近一段时间内的 5xx 错误日志。
    /// </summary>
    /// <param name="hours">向前回看的小时数。</param>
    /// <param name="limit">最多返回的记录条数。</param>
    /// <param name="cancellationToken">用于取消当前异步操作的令牌。</param>
    /// <returns>返回错误日志集合。</returns>
    Task<IReadOnlyList<ApiLogRecord>> GetRecentErrorsAsync(int hours, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按小时和接口维度聚合请求量、错误量和延迟指标。
    /// </summary>
    /// <param name="hours">向前回看的小时数。</param>
    /// <param name="limit">最多返回的聚合结果条数。</param>
    /// <param name="cancellationToken">用于取消当前异步操作的令牌。</param>
    /// <returns>返回接口统计结果集合。</returns>
    Task<IReadOnlyList<ApiEndpointStats>> GetApiStatsAsync(int hours, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// 统计指定天数范围内的日活用户趋势。
    /// </summary>
    /// <param name="days">向前回看的天数。</param>
    /// <param name="cancellationToken">用于取消当前异步操作的令牌。</param>
    /// <returns>返回每天的活跃用户统计。</returns>
    Task<IReadOnlyList<DailyActiveUserMetric>> GetDailyActiveUsersAsync(int days, CancellationToken cancellationToken = default);

    /// <summary>
    /// 统计指定天数范围内的浏览、点击、完成漏斗。
    /// </summary>
    /// <param name="days">向前回看的天数。</param>
    /// <param name="cancellationToken">用于取消当前异步操作的令牌。</param>
    /// <returns>返回漏斗统计结果。</returns>
    Task<FunnelMetric> GetFunnelAsync(int days, CancellationToken cancellationToken = default);
}
