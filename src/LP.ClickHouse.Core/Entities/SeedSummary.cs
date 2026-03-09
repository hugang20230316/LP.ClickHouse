namespace LP.ClickHouse.Core.Entities;

/// <summary>
/// 汇总一次示例造数请求实际写入的记录数。
/// </summary>
public class SeedSummary
{
    public int InsertedLogRows { get; set; }
    public int InsertedEventRows { get; set; }
    public int InsertedOrderRows { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
}
