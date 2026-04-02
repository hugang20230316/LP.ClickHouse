using System.Text;
using LP.ClickHouse.Core.Options;

namespace LP.ClickHouse.Core.Utilities;

public static class ClickHouseConnectionStringBuilder
{
    public static string Build(ClickHouseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.Host))
        {
            throw new ArgumentException("ClickHouse Host 不能为空", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.Database))
        {
            throw new ArgumentException("ClickHouse Database 不能为空", nameof(options));
        }

        var builder = new StringBuilder();
        builder.Append($"Host={options.Host};");
        builder.Append($"Port={options.Port};");
        builder.Append($"Protocol={options.Protocol};");
        builder.Append($"Database={options.Database};");
        builder.Append($"Username={options.Username};");
        builder.Append($"Password={options.Password};");

        if (!string.IsNullOrWhiteSpace(options.ClientName))
        {
            builder.Append($"Client Name={options.ClientName};");
        }

        builder.Append($"Compress={options.Compress};");
        builder.Append($"CheckCompressedHash={options.CheckCompressedHash};");
        builder.Append($"Command Timeout={options.CommandTimeoutSeconds};");

        return builder.ToString();
    }
}
