using Xunit;
using FluentAssertions;
using LP.ClickHouse.Core.Options;
using LP.ClickHouse.Core.Utilities;

namespace LP.ClickHouse.Tests;

public class ConnectionStringBuilderFullTests
{
    // ==================== 正常构建 ====================

    [Fact]
    public void Build_WithDefaultOptions_ShouldProduceValidConnectionString()
    {
        var options = new ClickHouseOptions();
        var result = ClickHouseConnectionStringBuilder.Build(options);

        result.Should().Contain("Host=localhost");
        result.Should().Contain("Port=8123");
        result.Should().Contain("Protocol=http");
        result.Should().Contain("Database=lp_observability");
        result.Should().Contain("Username=default");
        result.Should().Contain("Compress=True");
        result.Should().Contain("CheckCompressedHash=False");
        result.Should().Contain("Command Timeout=30");
    }

    [Fact]
    public void Build_WithCustomOptions_ShouldReflectAllValues()
    {
        var options = new ClickHouseOptions
        {
            Host = "10.0.0.1",
            Port = 9443,
            Protocol = "https",
            Database = "prod_analytics",
            Username = "ch_admin",
            Password = "s3cret!",
            Compress = false,
            CheckCompressedHash = true,
            CommandTimeoutSeconds = 90
        };

        var result = ClickHouseConnectionStringBuilder.Build(options);

        result.Should().Contain("Host=10.0.0.1");
        result.Should().Contain("Port=9443");
        result.Should().Contain("Protocol=https");
        result.Should().Contain("Database=prod_analytics");
        result.Should().Contain("Username=ch_admin");
        result.Should().Contain("Password=s3cret!");
        result.Should().Contain("Compress=False");
        result.Should().Contain("CheckCompressedHash=True");
        result.Should().Contain("Command Timeout=90");
    }

    [Fact]
    public void Build_ShouldEndEachSegmentWithSemicolon()
    {
        var options = new ClickHouseOptions();
        var result = ClickHouseConnectionStringBuilder.Build(options);

        // 连接字符串的每个键值对都应以分号分隔
        var segments = result.Split(';', StringSplitOptions.RemoveEmptyEntries);
        segments.Length.Should().Be(9);
    }

    [Fact]
    public void Build_WithEmptyPassword_ShouldIncludeEmptyPasswordSegment()
    {
        var options = new ClickHouseOptions { Password = string.Empty };
        var result = ClickHouseConnectionStringBuilder.Build(options);

        result.Should().Contain("Password=;");
    }

    // ==================== 参数校验 ====================

    [Fact]
    public void Build_WithNullOptions_ShouldThrowArgumentNullException()
    {
        var action = () => ClickHouseConnectionStringBuilder.Build(null!);
        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Build_WithEmptyHost_ShouldThrowArgumentException()
    {
        var options = new ClickHouseOptions { Host = "" };
        var action = () => ClickHouseConnectionStringBuilder.Build(options);
        action.Should().Throw<ArgumentException>().WithMessage("*Host*");
    }

    [Fact]
    public void Build_WithWhitespaceHost_ShouldThrowArgumentException()
    {
        var options = new ClickHouseOptions { Host = "   " };
        var action = () => ClickHouseConnectionStringBuilder.Build(options);
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Build_WithEmptyDatabase_ShouldThrowArgumentException()
    {
        var options = new ClickHouseOptions { Database = "" };
        var action = () => ClickHouseConnectionStringBuilder.Build(options);
        action.Should().Throw<ArgumentException>().WithMessage("*Database*");
    }

    [Fact]
    public void Build_WithWhitespaceDatabase_ShouldThrowArgumentException()
    {
        var options = new ClickHouseOptions { Database = "   " };
        var action = () => ClickHouseConnectionStringBuilder.Build(options);
        action.Should().Throw<ArgumentException>();
    }

    // ==================== 边界值 ====================

    [Fact]
    public void Build_WithZeroPort_ShouldStillBuild()
    {
        var options = new ClickHouseOptions { Port = 0 };
        var result = ClickHouseConnectionStringBuilder.Build(options);
        result.Should().Contain("Port=0");
    }

    [Fact]
    public void Build_WithZeroTimeout_ShouldStillBuild()
    {
        var options = new ClickHouseOptions { CommandTimeoutSeconds = 0 };
        var result = ClickHouseConnectionStringBuilder.Build(options);
        result.Should().Contain("Command Timeout=0");
    }
}
