namespace LP.ClickHouse.Core.Entities;

/// <summary>
/// 表示某一天的活跃用户统计结果。
/// </summary>
public class DailyActiveUserMetric
{
    public DateTime ActivityDate { get; set; }
    public long ActiveUsers { get; set; }
}
