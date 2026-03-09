namespace LP.ClickHouse.Core.Entities;

/// <summary>
/// 表示一条 API 错误或访问日志记录。
/// </summary>
public class ApiLogRecord
{
    public Guid LogId { get; set; }
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = string.Empty;
    public string ApiPath { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public ushort StatusCode { get; set; }
    public uint ResponseTimeMs { get; set; }
    public ulong UserId { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public string TraceId { get; set; } = string.Empty;
}
