using ClickHouse.Driver;
using LP.ClickHouse.Core.Builders;
using LP.ClickHouse.Core.Entities;
using LP.ClickHouse.Core.Options;
using Microsoft.Extensions.Options;

namespace LP.ClickHouse.Core.Services;

/// <summary>
/// 通过追加快照的方式演示 ClickHouse 场景下的业务语义 CRUD。
/// </summary>
public class OrderDemoService : IOrderDemoService
{
    private static readonly string[] OrderColumns = ["order_id", "user_id", "order_no", "status", "pay_amount", "city", "created_at", "updated_at", "version", "is_deleted"];

    private readonly ClickHouseClient _client;
    private readonly string _database;

    /// <summary>
    /// 使用共享客户端和配置初始化订单演示服务。
    /// </summary>
    /// <param name="client">已注册到容器中的 ClickHouse 客户端。</param>
    /// <param name="options">包含数据库名等连接配置的选项对象。</param>
    public OrderDemoService(ClickHouseClient client, IOptions<ClickHouseOptions> options)
    {
        _client = client;
        _database = SchemaSqlBuilder.SanitizeIdentifier(options.Value.Database);
    }

    /// <inheritdoc />
    public async Task<OrderSnapshotRecord> CreateAsync(OrderSnapshotRecord snapshot, CancellationToken cancellationToken = default)
    {
        if (snapshot.OrderId == 0 || snapshot.UserId == 0)
        {
            throw new ArgumentException("订单 ID 和用户 ID 必须大于 0", nameof(snapshot));
        }

        if (string.IsNullOrWhiteSpace(snapshot.OrderNo) || string.IsNullOrWhiteSpace(snapshot.Status) || string.IsNullOrWhiteSpace(snapshot.City))
        {
            throw new ArgumentException("订单号、状态和城市不能为空", nameof(snapshot));
        }

        var existing = await GetLatestAsync(snapshot.OrderId, cancellationToken);
        if (existing is not null)
        {
            throw new InvalidOperationException($"订单 {snapshot.OrderId} 已存在，不能重复创建");
        }

        var now = DateTime.UtcNow;
        var created = new OrderSnapshotRecord
        {
            OrderId = snapshot.OrderId,
            UserId = snapshot.UserId,
            OrderNo = snapshot.OrderNo,
            Status = snapshot.Status,
            PayAmount = snapshot.PayAmount,
            City = snapshot.City,
            CreatedAt = snapshot.CreatedAt == default ? now : snapshot.CreatedAt,
            UpdatedAt = snapshot.UpdatedAt == default ? now : snapshot.UpdatedAt,
            Version = 1,
            IsDeleted = false
        };

        await InsertAsync(created, cancellationToken);
        return created;
    }

    /// <inheritdoc />
    public async Task<OrderSnapshotRecord?> GetLatestAsync(ulong orderId, CancellationToken cancellationToken = default)
    {
        if (orderId == 0)
        {
            return null;
        }

        var sql = $@"
SELECT
    order_id,
    argMax(user_id, version) AS user_id,
    argMax(order_no, version) AS order_no,
    argMax(status, version) AS status,
    argMax(pay_amount, version) AS pay_amount,
    argMax(city, version) AS city,
    min(created_at) AS created_at,
    max(updated_at) AS updated_at,
    max(version) AS version,
    argMax(is_deleted, version) AS is_deleted
FROM {_database}.order_snapshots
WHERE order_id = {orderId}
GROUP BY order_id";

        using var reader = await _client.ExecuteReaderAsync(sql, null, null, cancellationToken);
        if (!reader.Read())
        {
            return null;
        }

        return MapOrderSnapshot(reader);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OrderSnapshotRecord>> GetHistoryAsync(ulong orderId, CancellationToken cancellationToken = default)
    {
        if (orderId == 0)
        {
            return [];
        }

        var sql = $@"
SELECT order_id, user_id, order_no, status, pay_amount, city, created_at, updated_at, version, is_deleted
FROM {_database}.order_snapshots
WHERE order_id = {orderId}
ORDER BY version";

        var results = new List<OrderSnapshotRecord>();
        using var reader = await _client.ExecuteReaderAsync(sql, null, null, cancellationToken);
        while (reader.Read())
        {
            results.Add(MapOrderSnapshot(reader));
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<OrderSnapshotRecord?> UpdateStatusAsync(ulong orderId, string newStatus, CancellationToken cancellationToken = default)
    {
        if (orderId == 0 || string.IsNullOrWhiteSpace(newStatus))
        {
            return null;
        }

        var latest = await GetLatestAsync(orderId, cancellationToken);
        if (latest is null || latest.IsDeleted)
        {
            return null;
        }

        var updated = new OrderSnapshotRecord
        {
            OrderId = latest.OrderId,
            UserId = latest.UserId,
            OrderNo = latest.OrderNo,
            Status = newStatus,
            PayAmount = latest.PayAmount,
            City = latest.City,
            CreatedAt = latest.CreatedAt,
            UpdatedAt = DateTime.UtcNow,
            Version = latest.Version + 1,
            IsDeleted = false
        };

        await InsertAsync(updated, cancellationToken);
        return updated;
    }

    /// <inheritdoc />
    public async Task<OrderSnapshotRecord?> MarkDeletedAsync(ulong orderId, CancellationToken cancellationToken = default)
    {
        if (orderId == 0)
        {
            return null;
        }

        var latest = await GetLatestAsync(orderId, cancellationToken);
        if (latest is null)
        {
            return null;
        }

        if (latest.IsDeleted)
        {
            return latest;
        }

        var deleted = new OrderSnapshotRecord
        {
            OrderId = latest.OrderId,
            UserId = latest.UserId,
            OrderNo = latest.OrderNo,
            Status = latest.Status,
            PayAmount = latest.PayAmount,
            City = latest.City,
            CreatedAt = latest.CreatedAt,
            UpdatedAt = DateTime.UtcNow,
            Version = latest.Version + 1,
            IsDeleted = true
        };

        await InsertAsync(deleted, cancellationToken);
        return deleted;
    }

    private async Task InsertAsync(OrderSnapshotRecord snapshot, CancellationToken cancellationToken)
    {
        var rows = new List<object[]>
        {
            new object[]
            {
                snapshot.OrderId,
                snapshot.UserId,
                snapshot.OrderNo,
                snapshot.Status,
                snapshot.PayAmount,
                snapshot.City,
                snapshot.CreatedAt,
                snapshot.UpdatedAt,
                snapshot.Version,
                (byte)(snapshot.IsDeleted ? 1 : 0)
            }
        };

        await _client.InsertBinaryAsync($"{_database}.order_snapshots", OrderColumns, rows, null, cancellationToken);
    }

    private static OrderSnapshotRecord MapOrderSnapshot(System.Data.Common.DbDataReader reader)
    {
        return new OrderSnapshotRecord
        {
            OrderId = Convert.ToUInt64(reader.GetValue(0)),
            UserId = Convert.ToUInt64(reader.GetValue(1)),
            OrderNo = reader.GetString(2),
            Status = reader.GetString(3),
            PayAmount = Convert.ToDecimal(reader.GetValue(4)),
            City = reader.GetString(5),
            CreatedAt = reader.GetDateTime(6),
            UpdatedAt = reader.GetDateTime(7),
            Version = Convert.ToUInt64(reader.GetValue(8)),
            IsDeleted = Convert.ToByte(reader.GetValue(9)) > 0
        };
    }
}

