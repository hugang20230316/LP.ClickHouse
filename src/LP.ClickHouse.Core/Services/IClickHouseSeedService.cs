using LP.ClickHouse.Core.Entities;

namespace LP.ClickHouse.Core.Services;

/// <summary>
/// 定义示例数据生成能力，方便接口和仪表盘开箱即用。
/// </summary>
public interface IClickHouseSeedService
{
    /// <summary>
    /// 生成并写入 API 日志、用户行为和订单快照示例数据。
    /// </summary>
    /// <param name="logCount">希望生成的 API 日志基础条数。</param>
    /// <param name="eventCount">希望生成的用户行为基础条数。</param>
    /// <param name="orderCount">希望生成的订单基础条数。</param>
    /// <param name="cancellationToken">用于取消当前异步操作的令牌。</param>
    /// <returns>返回本次写入的数据汇总信息。</returns>
    Task<SeedSummary> GenerateAsync(int logCount, int eventCount, int orderCount, CancellationToken cancellationToken = default);
}
