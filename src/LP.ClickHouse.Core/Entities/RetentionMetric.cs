namespace LP.ClickHouse.Core.Entities;

/// <summary>
/// 表示某个分群日期在特定天偏移上的留存结果。
/// </summary>
public class RetentionMetric
{
    public DateTime CohortDate { get; set; }
    public int DayOffset { get; set; }
    public long RetainedUsers { get; set; }
    public double RetentionRate { get; set; }
}
