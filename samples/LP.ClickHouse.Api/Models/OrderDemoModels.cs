namespace LP.ClickHouse.Api.Models;

/// <summary>
/// 创建订单初始快照时使用的请求体。
/// </summary>
public class CreateOrderRequest
{
    /// <summary>
    /// 订单 ID。
    /// </summary>
    public ulong OrderId { get; set; }

    /// <summary>
    /// 用户 ID。
    /// </summary>
    public ulong UserId { get; set; }

    /// <summary>
    /// 订单号。
    /// </summary>
    public string OrderNo { get; set; } = string.Empty;

    /// <summary>
    /// 初始状态。
    /// </summary>
    public string Status { get; set; } = "PendingPayment";

    /// <summary>
    /// 支付金额。
    /// </summary>
    public decimal PayAmount { get; set; }

    /// <summary>
    /// 所属城市。
    /// </summary>
    public string City { get; set; } = string.Empty;
}

/// <summary>
/// 更新订单状态时使用的请求体。
/// </summary>
public class UpdateOrderStatusRequest
{
    /// <summary>
    /// 订单 ID。
    /// </summary>
    public ulong OrderId { get; set; }

    /// <summary>
    /// 新的订单状态。
    /// </summary>
    public string NewStatus { get; set; } = string.Empty;
}

/// <summary>
/// 逻辑删除订单时使用的请求体。
/// </summary>
public class DeleteOrderRequest
{
    /// <summary>
    /// 订单 ID。
    /// </summary>
    public ulong OrderId { get; set; }
}
