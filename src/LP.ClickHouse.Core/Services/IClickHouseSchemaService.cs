namespace LP.ClickHouse.Core.Services;

/// <summary>
/// 定义 ClickHouse 连通性检查和初始化建表能力。
/// </summary>
public interface IClickHouseSchemaService
{
    /// <summary>
    /// 检查 ClickHouse 服务是否可达。
    /// </summary>
    /// <param name="cancellationToken">用于取消当前异步操作的令牌。</param>
    /// <returns>可达时返回 `true`，否则返回 `false`。</returns>
    Task<bool> PingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 初始化示例项目所需的数据库和表结构。
    /// </summary>
    /// <param name="cancellationToken">用于取消当前异步操作的令牌。</param>
    /// <returns>表示异步初始化流程的任务。</returns>
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
