using LP.ClickHouse.Core.Entities;

namespace LP.ClickHouse.Core.Services;

/// <summary>
/// 定义复杂分析场景的查询能力。
/// </summary>
public interface IAdvancedAnalyticsService
{
    /// <summary>
    /// 查询高流量且高错误率的慢接口排行。
    /// </summary>
    /// <param name="days">向前回看的天数。</param>
    /// <param name="limit">最多返回的记录条数。</param>
    /// <param name="minRequestCount">最小请求量门槛。</param>
    /// <param name="minErrorRate">最小错误率门槛，单位为百分比。</param>
    /// <param name="cancellationToken">用于取消当前异步操作的令牌。</param>
    /// <returns>返回慢接口聚合结果集合。</returns>
    Task<IReadOnlyList<SlowApiMetric>> GetSlowApisAsync(int days, int limit, int minRequestCount, double minErrorRate, CancellationToken cancellationToken = default);

    /// <summary>
    /// 查询近一段时间的分群留存结果。
    /// </summary>
    /// <param name="lookbackDays">向前回看的分群天数范围。</param>
    /// <param name="cancellationToken">用于取消当前异步操作的令牌。</param>
    /// <returns>返回分群留存集合。</returns>
    Task<IReadOnlyList<RetentionMetric>> GetRetentionAsync(int lookbackDays, CancellationToken cancellationToken = default);

    /// <summary>
    /// 查询按顺序统计的路径漏斗结果。
    /// </summary>
    /// <param name="days">向前回看的天数。</param>
    /// <param name="windowSeconds">漏斗窗口时长，单位秒。</param>
    /// <param name="cancellationToken">用于取消当前异步操作的令牌。</param>
    /// <returns>返回各漏斗层级的用户数量。</returns>
    Task<IReadOnlyList<PathFunnelMetric>> GetPathFunnelAsync(int days, int windowSeconds, CancellationToken cancellationToken = default);
}
