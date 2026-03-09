using LP.ClickHouse.Core.Entities;

namespace LP.ClickHouse.Core.Services;

/// <summary>
/// 定义订单快照教学场景需要的准 CRUD 能力。
/// </summary>
public interface IOrderDemoService
{
    /// <summary>
    /// 创建一条初始订单快照。
    /// </summary>
    /// <param name="snapshot">待创建的订单快照信息。</param>
    /// <param name="cancellationToken">用于取消当前异步操作的令牌。</param>
    /// <returns>返回实际写入的初始快照。</returns>
    Task<OrderSnapshotRecord> CreateAsync(OrderSnapshotRecord snapshot, CancellationToken cancellationToken = default);

    /// <summary>
    /// 查询指定订单的最新快照。
    /// </summary>
    /// <param name="orderId">订单 ID。</param>
    /// <param name="cancellationToken">用于取消当前异步操作的令牌。</param>
    /// <returns>返回最新快照；不存在时返回 <c>null</c>。</returns>
    Task<OrderSnapshotRecord?> GetLatestAsync(ulong orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 查询指定订单的历史版本列表。
    /// </summary>
    /// <param name="orderId">订单 ID。</param>
    /// <param name="cancellationToken">用于取消当前异步操作的令牌。</param>
    /// <returns>返回按版本升序排列的快照列表。</returns>
    Task<IReadOnlyList<OrderSnapshotRecord>> GetHistoryAsync(ulong orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 为指定订单追加一条新的状态快照。
    /// </summary>
    /// <param name="orderId">订单 ID。</param>
    /// <param name="newStatus">新的订单状态。</param>
    /// <param name="cancellationToken">用于取消当前异步操作的令牌。</param>
    /// <returns>返回新写入的快照；找不到订单时返回 <c>null</c>。</returns>
    Task<OrderSnapshotRecord?> UpdateStatusAsync(ulong orderId, string newStatus, CancellationToken cancellationToken = default);

    /// <summary>
    /// 通过追加删除快照的方式逻辑删除订单。
    /// </summary>
    /// <param name="orderId">订单 ID。</param>
    /// <param name="cancellationToken">用于取消当前异步操作的令牌。</param>
    /// <returns>返回最新删除快照；找不到订单时返回 <c>null</c>。</returns>
    Task<OrderSnapshotRecord?> MarkDeletedAsync(ulong orderId, CancellationToken cancellationToken = default);
}
