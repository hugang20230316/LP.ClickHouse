namespace LP.ClickHouse.Core.Entities;

/// <summary>
/// 表示浏览、点击、完成三步漏斗的整体统计结果。
/// </summary>
public class FunnelMetric
{
    public long ViewedCount { get; set; }
    public long ClickedCount { get; set; }
    public long CompletedCount { get; set; }
    public double ViewToClickRate { get; set; }
    public double ClickToCompletionRate { get; set; }
}
