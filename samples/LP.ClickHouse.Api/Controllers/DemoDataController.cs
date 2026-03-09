using LP.ClickHouse.Api.Models;
using LP.ClickHouse.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace LP.ClickHouse.Api.Controllers;

/// <summary>
/// 生成示例数据，让 ClickHouse 项目启动后马上可以演示。
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class DemoDataController : ControllerBase
{
    private readonly IClickHouseSeedService _seedService;

    /// <summary>
    /// 使用造数服务初始化控制器。
    /// </summary>
    /// <param name="seedService">负责写入示例数据的服务实例。</param>
    public DemoDataController(IClickHouseSeedService seedService) => _seedService = seedService;

    /// <summary>
    /// 按请求参数或默认值生成 API 日志、用户行为和订单快照示例数据。
    /// </summary>
    /// <param name="request">包含日志数、事件数和订单数的请求体。</param>
    /// <param name="cancellationToken">用于取消当前异步操作的令牌。</param>
    /// <returns>返回本次造数的汇总结果。</returns>
    [HttpPost("seed")]
    public async Task<IActionResult> Seed([FromBody] SeedRequest? request, CancellationToken cancellationToken)
        => Ok(await _seedService.GenerateAsync(request?.LogCount ?? 5000, request?.EventCount ?? 10000, request?.OrderCount ?? 2000, cancellationToken));
}
