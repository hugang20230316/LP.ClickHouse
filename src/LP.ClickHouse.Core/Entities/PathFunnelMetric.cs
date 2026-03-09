namespace LP.ClickHouse.Core.Entities;

/// <summary>
/// 表示路径漏斗中某一步的用户数量。
/// </summary>
public class PathFunnelMetric
{
    public int Level { get; set; }
    public string StepName { get; set; } = string.Empty;
    public long UserCount { get; set; }
}
