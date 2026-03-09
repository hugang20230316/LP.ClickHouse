namespace LP.ClickHouse.Core.Entities;

/// <summary>
/// 表示慢接口排行中的单条聚合结果。
/// </summary>
public class SlowApiMetric
{
    public string ApiPath { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public long RequestCount { get; set; }
    public long ErrorCount { get; set; }
    public double ErrorRate { get; set; }
    public double AvgResponseTimeMs { get; set; }
    public double P95ResponseTimeMs { get; set; }
}
