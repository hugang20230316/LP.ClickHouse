using FluentAssertions;
using LP.ClickHouse.Core.Options;
using LP.ClickHouse.Core.Utilities;
using Xunit;

namespace LP.ClickHouse.Tests;

public class ConnectionStringBuilderTests
{
    [Fact]
    public void Build_ShouldIncludeConfiguredValues()
    {
        var options = new ClickHouseOptions { Host = "127.0.0.1", Port = 8123, Protocol = "http", Database = "analytics", Username = "default", Password = "secret", ClientName = "LP.ClickHouse.Tests", Compress = true, CheckCompressedHash = false, CommandTimeoutSeconds = 45 };
        var connectionString = ClickHouseConnectionStringBuilder.Build(options);
        connectionString.Should().Contain("Host=127.0.0.1");
        connectionString.Should().Contain("Port=8123");
        connectionString.Should().Contain("Database=analytics");
        connectionString.Should().Contain("Username=default");
        connectionString.Should().Contain("Password=secret");
        connectionString.Should().Contain("Client Name=LP.ClickHouse.Tests");
        connectionString.Should().Contain("Command Timeout=45");
    }
}
