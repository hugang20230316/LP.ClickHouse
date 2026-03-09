using ClickHouse.Driver;
using LP.ClickHouse.Core.Builders;
using LP.ClickHouse.Core.Options;
using Microsoft.Extensions.Options;

namespace LP.ClickHouse.Core.Services;

/// <summary>
/// 负责连通性检查，并创建示例项目依赖的最小表结构。
/// </summary>
public class ClickHouseSchemaService : IClickHouseSchemaService
{
    private readonly ClickHouseClient _client;
    private readonly string _database;

    /// <summary>
    /// 使用共享客户端和配置初始化建表服务。
    /// </summary>
    /// <param name="client">已注册到容器中的 ClickHouse 客户端。</param>
    /// <param name="options">包含数据库名等连接配置的选项对象。</param>
    public ClickHouseSchemaService(ClickHouseClient client, IOptions<ClickHouseOptions> options)
    {
        _client = client;
        _database = SchemaSqlBuilder.SanitizeIdentifier(options.Value.Database);
    }

    /// <summary>
    /// 调用驱动自带的 Ping 能力，而不是手写 SQL，以便更早发现连接层问题。
    /// </summary>
    /// <param name="cancellationToken">用于取消当前异步操作的令牌。</param>
    /// <returns>连接可用时返回 <c>true</c>。</returns>
    public Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        return _client.PingAsync(null, cancellationToken);
    }

    /// <summary>
    /// 先建库，再依次创建日志表、事件表和订单快照表，保证依赖顺序正确。
    /// </summary>
    /// <param name="cancellationToken">用于取消当前异步操作的令牌。</param>
    /// <returns>表示异步初始化流程的任务。</returns>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _client.ExecuteNonQueryAsync(SchemaSqlBuilder.BuildCreateDatabaseSql(_database), null, null, cancellationToken);
        await _client.ExecuteNonQueryAsync(SchemaSqlBuilder.BuildCreateApiLogsTableSql(_database), null, null, cancellationToken);
        await _client.ExecuteNonQueryAsync(SchemaSqlBuilder.BuildCreateUserEventsTableSql(_database), null, null, cancellationToken);
        await _client.ExecuteNonQueryAsync(SchemaSqlBuilder.BuildCreateOrderSnapshotsTableSql(_database), null, null, cancellationToken);
    }
}
