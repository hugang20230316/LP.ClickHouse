using LP.ClickHouse.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace LP.ClickHouse.Api.Controllers;

/// <summary>
/// 暴露基于 ClickHouse 的只读分析接口。
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AnalyticsController : ControllerBase
{
    private readonly ILogAnalyticsService _analyticsService;

    /// <summary>
    /// 使用分析服务初始化控制器。
    /// </summary>
    /// <param name="analyticsService">封装 ClickHouse 分析查询的服务实例。</param>
    public AnalyticsController(ILogAnalyticsService analyticsService) => _analyticsService = analyticsService;

    /// <summary>
    /// 返回指定时间窗口内的 5xx API 错误记录。
    /// </summary>
    /// <param name="hours">向前回看的小时数。</param>
    /// <param name="limit">最多返回的记录条数。</param>
    /// <param name="cancellationToken">用于取消当前异步操作的令牌。</param>
    /// <returns>返回错误日志查询结果。</returns>
    [HttpGet("recent-errors")]
    public async Task<IActionResult> GetRecentErrors([FromQuery] int hours = 1, [FromQuery] int limit = 100, CancellationToken cancellationToken = default)
        => Ok(await _analyticsService.GetRecentErrorsAsync(hours, limit, cancellationToken));

    /// <summary>
    /// 返回接口维度的请求量、错误量和延迟统计。
    /// </summary>
    /// <param name="hours">向前回看的小时数。</param>
    /// <param name="limit">最多返回的聚合结果条数。</param>
    /// <param name="cancellationToken">用于取消当前异步操作的令牌。</param>
    /// <returns>返回接口统计查询结果。</returns>
    [HttpGet("api-stats")]
    public async Task<IActionResult> GetApiStats([FromQuery] int hours = 24, [FromQuery] int limit = 50, CancellationToken cancellationToken = default)
        => Ok(await _analyticsService.GetApiStatsAsync(hours, limit, cancellationToken));

    /// <summary>
    /// 返回日活用户趋势数据。
    /// </summary>
    /// <param name="days">向前回看的天数。</param>
    /// <param name="cancellationToken">用于取消当前异步操作的令牌。</param>
    /// <returns>返回日活查询结果。</returns>
    [HttpGet("dau")]
    public async Task<IActionResult> GetDailyActiveUsers([FromQuery] int days = 7, CancellationToken cancellationToken = default)
        => Ok(await _analyticsService.GetDailyActiveUsersAsync(days, cancellationToken));

    /// <summary>
    /// 返回浏览、点击、完成的漏斗统计结果。
    /// </summary>
    /// <param name="days">向前回看的天数。</param>
    /// <param name="cancellationToken">用于取消当前异步操作的令牌。</param>
    /// <returns>返回漏斗查询结果。</returns>
    [HttpGet("funnel")]
    public async Task<IActionResult> GetFunnel([FromQuery] int days = 7, CancellationToken cancellationToken = default)
        => Ok(await _analyticsService.GetFunnelAsync(days, cancellationToken));
}
