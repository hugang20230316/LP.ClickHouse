using LP.ClickHouse.Api.Models;
using LP.ClickHouse.Core.Entities;
using LP.ClickHouse.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace LP.ClickHouse.Api.Controllers;

/// <summary>
/// 暴露订单快照场景下的准 CRUD 示例接口。
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class OrderDemoController : ControllerBase
{
    private readonly IOrderDemoService _orderDemoService;

    /// <summary>
    /// 使用订单演示服务初始化控制器。
    /// </summary>
    /// <param name="orderDemoService">负责订单快照读写的服务实例。</param>
    public OrderDemoController(IOrderDemoService orderDemoService) => _orderDemoService = orderDemoService;

    /// <summary>
    /// 创建订单的第一版快照。
    /// </summary>
    /// <param name="request">创建订单请求体。</param>
    /// <param name="cancellationToken">用于取消当前异步操作的令牌。</param>
    /// <returns>返回新建订单快照。</returns>
    [HttpPost("create")]
    public async Task<IActionResult> Create([FromBody] CreateOrderRequest request, CancellationToken cancellationToken)
    {
        if (request.OrderId == 0 || request.UserId == 0 || string.IsNullOrWhiteSpace(request.OrderNo) || string.IsNullOrWhiteSpace(request.Status) || string.IsNullOrWhiteSpace(request.City))
        {
            return BadRequest(new { message = "订单 ID、用户 ID、订单号、状态和城市不能为空。" });
        }

        try
        {
            var created = await _orderDemoService.CreateAsync(new OrderSnapshotRecord
            {
                OrderId = request.OrderId,
                UserId = request.UserId,
                OrderNo = request.OrderNo,
                Status = request.Status,
                PayAmount = request.PayAmount,
                City = request.City
            }, cancellationToken);

            return Ok(created);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }

    /// <summary>
    /// 查询指定订单的最新快照。
    /// </summary>
    /// <param name="orderId">订单 ID。</param>
    /// <param name="cancellationToken">用于取消当前异步操作的令牌。</param>
    /// <returns>返回最新快照；找不到时返回 404。</returns>
    [HttpGet("latest")]
    public async Task<IActionResult> GetLatest([FromQuery] ulong orderId, CancellationToken cancellationToken)
    {
        if (orderId == 0)
        {
            return BadRequest(new { message = "orderId 必须大于 0。" });
        }

        var result = await _orderDemoService.GetLatestAsync(orderId, cancellationToken);
        return result is null ? NotFound(new { message = $"订单 {orderId} 不存在。" }) : Ok(result);
    }

    /// <summary>
    /// 查询指定订单的历史版本链路。
    /// </summary>
    /// <param name="orderId">订单 ID。</param>
    /// <param name="cancellationToken">用于取消当前异步操作的令牌。</param>
    /// <returns>返回历史快照列表。</returns>
    [HttpGet("history")]
    public async Task<IActionResult> GetHistory([FromQuery] ulong orderId, CancellationToken cancellationToken)
    {
        if (orderId == 0)
        {
            return BadRequest(new { message = "orderId 必须大于 0。" });
        }

        var result = await _orderDemoService.GetHistoryAsync(orderId, cancellationToken);
        return result.Count == 0 ? NotFound(new { message = $"订单 {orderId} 不存在。" }) : Ok(result);
    }

    /// <summary>
    /// 为指定订单追加一条新的状态快照。
    /// </summary>
    /// <param name="request">状态更新请求体。</param>
    /// <param name="cancellationToken">用于取消当前异步操作的令牌。</param>
    /// <returns>返回新写入的订单快照。</returns>
    [HttpPost("update-status")]
    public async Task<IActionResult> UpdateStatus([FromBody] UpdateOrderStatusRequest request, CancellationToken cancellationToken)
    {
        if (request.OrderId == 0 || string.IsNullOrWhiteSpace(request.NewStatus))
        {
            return BadRequest(new { message = "订单 ID 和新的状态不能为空。" });
        }

        var result = await _orderDemoService.UpdateStatusAsync(request.OrderId, request.NewStatus, cancellationToken);
        return result is null ? NotFound(new { message = $"订单 {request.OrderId} 不存在，或已被删除。" }) : Ok(result);
    }

    /// <summary>
    /// 通过追加删除快照的方式逻辑删除订单。
    /// </summary>
    /// <param name="request">删除请求体。</param>
    /// <param name="cancellationToken">用于取消当前异步操作的令牌。</param>
    /// <returns>返回删除后的最新快照。</returns>
    [HttpPost("delete")]
    public async Task<IActionResult> Delete([FromBody] DeleteOrderRequest request, CancellationToken cancellationToken)
    {
        if (request.OrderId == 0)
        {
            return BadRequest(new { message = "订单 ID 不能为空。" });
        }

        var result = await _orderDemoService.MarkDeletedAsync(request.OrderId, cancellationToken);
        return result is null ? NotFound(new { message = $"订单 {request.OrderId} 不存在。" }) : Ok(result);
    }
}
