using LP.ClickHouse.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace LP.ClickHouse.Api.Controllers;

/// <summary>
/// 暴露 ClickHouse 连通性检查和初始化接口。
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SchemaController : ControllerBase
{
    private readonly IClickHouseSchemaService _schemaService;

    /// <summary>
    /// 使用建表服务初始化控制器。
    /// </summary>
    /// <param name="schemaService">负责连通性检查和初始化建表的服务实例。</param>
    public SchemaController(IClickHouseSchemaService schemaService) => _schemaService = schemaService;

    /// <summary>
    /// 检查 ClickHouse 是否可用。
    /// </summary>
    /// <param name="cancellationToken">用于取消当前异步操作的令牌。</param>
    /// <returns>返回连通性检查结果。</returns>
    [HttpGet("ping")]
    public async Task<IActionResult> Ping(CancellationToken cancellationToken) => Ok(new { success = await _schemaService.PingAsync(cancellationToken) });

    /// <summary>
    /// 创建示例项目所需的数据库和表结构。
    /// </summary>
    /// <param name="cancellationToken">用于取消当前异步操作的令牌。</param>
    /// <returns>返回初始化完成消息。</returns>
    [HttpPost("init")]
    public async Task<IActionResult> Initialize(CancellationToken cancellationToken)
    {
        await _schemaService.InitializeAsync(cancellationToken);
        return Ok(new { message = "ClickHouse 数据库和示例表已初始化" });
    }
}
