namespace LP.ClickHouse.Api.Models;

/// <summary>
/// 造数接口使用的请求体。
/// </summary>
public class SeedRequest
{
    /// <summary>
    /// 希望生成的 API 日志基础条数。
    /// </summary>
    public int LogCount { get; set; } = 5000;

    /// <summary>
    /// 希望生成的用户行为基础条数。
    /// </summary>
    public int EventCount { get; set; } = 10000;

    /// <summary>
    /// 希望生成的订单基础条数。
    /// </summary>
    public int OrderCount { get; set; } = 2000;
}
