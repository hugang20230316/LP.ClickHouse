namespace LP.ClickHouse.Core.Entities;

/// <summary>
/// 表示订单快照表中的一条版本记录。
/// </summary>
public class OrderSnapshotRecord
{
    public ulong OrderId { get; set; }
    public ulong UserId { get; set; }
    public string OrderNo { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal PayAmount { get; set; }
    public string City { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ulong Version { get; set; }
    public bool IsDeleted { get; set; }
}
