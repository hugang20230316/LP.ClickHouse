namespace LP.ClickHouse.Core.Options;

/// <summary>
/// 用于构建 ClickHouse 连接字符串的强类型配置。
/// </summary>
public class ClickHouseOptions
{
    /// <summary>
    /// `appsettings` 中对应的配置节名称。
    /// </summary>
    public const string SectionName = "ClickHouse";

    /// <summary>
    /// ClickHouse 服务主机名。
    /// </summary>
    public string Host { get; set; } = "localhost";

    /// <summary>
    /// 示例项目默认使用的 ClickHouse HTTP 端口。
    /// </summary>
    public int Port { get; set; } = 8123;

    /// <summary>
    /// 连接协议，默认是本地 Docker 场景常用的 http。
    /// </summary>
    public string Protocol { get; set; } = "http";

    /// <summary>
    /// 用于保存观测数据示例表的默认数据库名。
    /// </summary>
    public string Database { get; set; } = "lp_observability";

    /// <summary>
    /// ClickHouse 登录用户名。
    /// </summary>
    public string Username { get; set; } = "default";

    /// <summary>
    /// ClickHouse 登录密码。
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// 是否启用 HTTP 传输压缩，用于减少网络传输体积。
    /// </summary>
    public bool Compress { get; set; } = true;

    /// <summary>
    /// 是否校验压缩负载的哈希值。
    /// </summary>
    public bool CheckCompressedHash { get; set; } = false;

    /// <summary>
    /// 建表、写入和查询操作的超时时间，单位为秒。
    /// </summary>
    public int CommandTimeoutSeconds { get; set; } = 30;
}
