using ClickHouse.Driver;
using LP.ClickHouse.Core.Builders;
using LP.ClickHouse.Core.Entities;
using LP.ClickHouse.Core.Options;
using Microsoft.Extensions.Options;

namespace LP.ClickHouse.Core.Services;

/// <summary>
/// 生成形态稳定的示例数据，让 API 查询和 Grafana 面板都能马上看到趋势。
/// </summary>
public class ClickHouseSeedService : IClickHouseSeedService
{
    private static readonly string[] LogColumns = ["log_id", "timestamp", "level", "api_path", "method", "status_code", "response_time_ms", "user_id", "ip_address", "error_message", "trace_id"];
    private static readonly string[] EventColumns = ["event_time", "user_id", "session_id", "event_type", "page", "device", "trace_id"];
    private static readonly string[] OrderColumns = ["order_id", "user_id", "order_no", "status", "pay_amount", "city", "created_at", "updated_at", "version", "is_deleted"];
    private static readonly string[] Levels = ["Info", "Warning", "Error"];
    private static readonly string[] Paths = ["/api/orders", "/api/payments", "/api/reports", "/api/students", "/api/teachers"];
    private static readonly string[] Methods = ["GET", "POST", "PUT"];
    private static readonly string[] Devices = ["web", "ios", "android"];
    private static readonly string[] Pages = ["market", "trade", "checkout", "orders"];
    private static readonly string[] EventTypes = ["viewed_market", "clicked_trade", "completed_trade"];
    private static readonly string[] OrderStatuses = ["PendingPayment", "Paid", "Shipped", "Completed"];
    private static readonly string[] Cities = ["上海", "北京", "深圳", "杭州", "武汉"];

    private readonly ClickHouseClient _client;
    private readonly string _database;

    /// <summary>
    /// 使用共享客户端和配置初始化造数服务。
    /// </summary>
    /// <param name="client">已注册到容器中的 ClickHouse 客户端。</param>
    /// <param name="options">包含数据库名等连接配置的选项对象。</param>
    public ClickHouseSeedService(ClickHouseClient client, IOptions<ClickHouseOptions> options)
    {
        _client = client;
        _database = SchemaSqlBuilder.SanitizeIdentifier(options.Value.Database);
    }

    /// <summary>
    /// 生成 API 日志、用户行为与订单快照示例数据，并批量写入 ClickHouse。
    /// </summary>
    /// <param name="logCount">希望生成的 API 日志基础条数。</param>
    /// <param name="eventCount">希望生成的用户行为基础条数。</param>
    /// <param name="orderCount">希望生成的订单基础条数。</param>
    /// <param name="cancellationToken">用于取消当前异步操作的令牌。</param>
    /// <returns>返回本次写入的数据汇总信息。</returns>
    public async Task<SeedSummary> GenerateAsync(int logCount, int eventCount, int orderCount, CancellationToken cancellationToken = default)
    {
        var safeLogCount = Math.Clamp(logCount, 1, 50000);
        var safeEventCount = Math.Clamp(eventCount, 1, 100000);
        var safeOrderCount = Math.Clamp(orderCount, 1, 20000);
        var random = new Random();

        var logRows = BuildLogRows(safeLogCount, random);
        var eventRows = BuildEventRows(safeEventCount, random);
        var orderRows = BuildOrderRows(safeOrderCount, random);

        await _client.InsertBinaryAsync($"{_database}.api_logs", LogColumns, logRows, null, cancellationToken);
        await _client.InsertBinaryAsync($"{_database}.user_events", EventColumns, eventRows, null, cancellationToken);
        await _client.InsertBinaryAsync($"{_database}.order_snapshots", OrderColumns, orderRows, null, cancellationToken);

        return new SeedSummary
        {
            InsertedLogRows = logRows.Count,
            InsertedEventRows = eventRows.Count,
            InsertedOrderRows = orderRows.Count,
            GeneratedAtUtc = DateTime.UtcNow
        };
    }

    private static List<object[]> BuildLogRows(int count, Random random)
    {
        var rows = new List<object[]>(count);
        for (var index = 0; index < count; index++)
        {
            var statusCode = random.NextDouble() < 0.15 ? 500 + random.Next(0, 4) : 200 + random.Next(0, 30);
            var level = statusCode >= 500 ? "Error" : Levels[random.Next(Levels.Length)];
            rows.Add([
                Guid.NewGuid(),
                DateTime.UtcNow.AddMinutes(-random.Next(0, 60 * 24 * 7)),
                level,
                Paths[random.Next(Paths.Length)],
                Methods[random.Next(Methods.Length)],
                (ushort)statusCode,
                (uint)random.Next(20, 1800),
                (ulong)random.NextInt64(1000, 5000),
                $"10.0.{random.Next(0, 255)}.{random.Next(1, 255)}",
                statusCode >= 500 ? "upstream timeout" : string.Empty,
                Guid.NewGuid().ToString("N")
            ]);
        }

        return rows;
    }

    private static List<object[]> BuildEventRows(int count, Random random)
    {
        var rows = new List<object[]>(count);
        for (var index = 0; index < count; index++)
        {
            var sessionId = Guid.NewGuid().ToString("N");
            var userId = (ulong)random.NextInt64(1000, 6000);
            var baseTime = DateTime.UtcNow.AddMinutes(-random.Next(0, 60 * 24 * 14));
            var page = Pages[random.Next(Pages.Length)];
            var device = Devices[random.Next(Devices.Length)];

            rows.Add([baseTime, userId, sessionId, EventTypes[0], page, device, Guid.NewGuid().ToString("N")]);
            if (random.NextDouble() < 0.62) rows.Add([baseTime.AddSeconds(random.Next(1, 60)), userId, sessionId, EventTypes[1], page, device, Guid.NewGuid().ToString("N")]);
            if (random.NextDouble() < 0.31) rows.Add([baseTime.AddSeconds(random.Next(60, 180)), userId, sessionId, EventTypes[2], page, device, Guid.NewGuid().ToString("N")]);
        }

        return rows;
    }

    private static List<object[]> BuildOrderRows(int count, Random random)
    {
        var rows = new List<object[]>(count * 4);
        const ulong startOrderId = 202603090000000;

        for (var index = 0; index < count; index++)
        {
            var orderId = startOrderId + (ulong)index;
            var userId = (ulong)random.NextInt64(1000, 6000);
            var orderNo = $"ORD{orderId}";
            var amount = Math.Round((decimal)(random.NextDouble() * 480 + 20), 2);
            var city = Cities[random.Next(Cities.Length)];
            var createdAt = DateTime.UtcNow.AddMinutes(-random.Next(0, 60 * 24 * 14));

            rows.Add([orderId, userId, orderNo, OrderStatuses[0], amount, city, createdAt, createdAt, 1UL, (byte)0]);

            var currentVersion = 1UL;
            if (random.NextDouble() < 0.82)
            {
                currentVersion++;
                rows.Add([orderId, userId, orderNo, OrderStatuses[1], amount, city, createdAt, createdAt.AddMinutes(5), currentVersion, (byte)0]);
            }

            if (currentVersion >= 2 && random.NextDouble() < 0.67)
            {
                currentVersion++;
                rows.Add([orderId, userId, orderNo, OrderStatuses[2], amount, city, createdAt, createdAt.AddHours(3), currentVersion, (byte)0]);
            }

            if (currentVersion >= 3 && random.NextDouble() < 0.54)
            {
                currentVersion++;
                rows.Add([orderId, userId, orderNo, OrderStatuses[3], amount, city, createdAt, createdAt.AddHours(24), currentVersion, (byte)0]);
            }

            if (random.NextDouble() < 0.05)
            {
                currentVersion++;
                rows.Add([orderId, userId, orderNo, OrderStatuses[Math.Min((int)currentVersion - 2, OrderStatuses.Length - 1)], amount, city, createdAt, createdAt.AddHours(30), currentVersion, (byte)1]);
            }
        }

        return rows;
    }
}
