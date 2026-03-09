using LP.ClickHouse.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace LP.ClickHouse.Api.Controllers;

/// <summary>
/// 暴露教程中的复杂查询示例接口。
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AdvancedAnalyticsController : ControllerBase
{
    private readonly IAdvancedAnalyticsService _advancedAnalyticsService;

    /// <summary>
    /// 使用复杂分析服务初始化控制器。
    /// </summary>
    /// <param name="advancedAnalyticsService">负责执行复杂查询的服务实例。</param>
    public AdvancedAnalyticsController(IAdvancedAnalyticsService advancedAnalyticsService) => _advancedAnalyticsService = advancedAnalyticsService;

    /// <summary>
    /// 返回高流量且高错误率的慢接口排行。
    /// </summary>
    /// <param name="days">向前回看的天数。</param>
    /// <param name="limit">最多返回的记录条数。</param>
    /// <param name="minRequestCount">最小请求量门槛。</param>
    /// <param name="minErrorRate">最小错误率门槛，单位为百分比。</param>
    /// <param name="cancellationToken">用于取消当前异步操作的令牌。</param>
    /// <returns>返回慢接口排行结果。</returns>
    [HttpGet("slow-apis")]
    public async Task<IActionResult> GetSlowApis([FromQuery] int days = 7, [FromQuery] int limit = 20, [FromQuery] int minRequestCount = 1000, [FromQuery] double minErrorRate = 1, CancellationToken cancellationToken = default)
        => Ok(await _advancedAnalyticsService.GetSlowApisAsync(days, limit, minRequestCount, minErrorRate, cancellationToken));

    /// <summary>
    /// 返回近一段时间的分群留存结果。
    /// </summary>
    /// <param name="lookbackDays">向前回看的分群天数范围。</param>
    /// <param name="cancellationToken">用于取消当前异步操作的令牌。</param>
    /// <returns>返回留存结果集合。</returns>
    [HttpGet("retention")]
    public async Task<IActionResult> GetRetention([FromQuery] int lookbackDays = 14, CancellationToken cancellationToken = default)
        => Ok(await _advancedAnalyticsService.GetRetentionAsync(lookbackDays, cancellationToken));

    /// <summary>
    /// 返回按顺序统计的路径漏斗结果。
    /// </summary>
    /// <param name="days">向前回看的天数。</param>
    /// <param name="windowSeconds">漏斗窗口时长，单位秒。</param>
    /// <param name="cancellationToken">用于取消当前异步操作的令牌。</param>
    /// <returns>返回漏斗层级统计结果。</returns>
    [HttpGet("path-funnel")]
    public async Task<IActionResult> GetPathFunnel([FromQuery] int days = 7, [FromQuery] int windowSeconds = 3600, CancellationToken cancellationToken = default)
        => Ok(await _advancedAnalyticsService.GetPathFunnelAsync(days, windowSeconds, cancellationToken));
}
