namespace LP.ClickHouse.Core.Entities;

/// <summary>
/// 表示按时间桶和接口维度聚合后的访问统计结果。
/// </summary>
public class ApiEndpointStats
{
    public DateTime BucketStart { get; set; }
    public string ApiPath { get; set; } = string.Empty;
    public long RequestCount { get; set; }
    public long ErrorCount { get; set; }
    public double AvgResponseTimeMs { get; set; }
    public double P95ResponseTimeMs { get; set; }
}
