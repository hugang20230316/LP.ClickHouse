using FluentAssertions;
using LP.ClickHouse.Core.Options;
using Xunit;

namespace LP.ClickHouse.Tests;

public class ClickHouseOptionsTests
{
    [Fact]
    public void DefaultValues_ShouldMatchExpected()
    {
        var options = new ClickHouseOptions();

        options.Host.Should().Be("localhost");
        options.Port.Should().Be(8123);
        options.Protocol.Should().Be("http");
        options.Database.Should().Be("lp_observability");
        options.Username.Should().Be("default");
        options.Password.Should().BeEmpty();
        options.ClientName.Should().BeEmpty();
        options.Compress.Should().BeTrue();
        options.CheckCompressedHash.Should().BeFalse();
        options.CommandTimeoutSeconds.Should().Be(30);
    }

    [Fact]
    public void SectionName_ShouldBeClickHouse()
    {
        ClickHouseOptions.SectionName.Should().Be("ClickHouse");
    }

    [Fact]
    public void Properties_ShouldBeSettable()
    {
        var options = new ClickHouseOptions
        {
            Host = "192.168.1.100",
            Port = 9000,
            Protocol = "https",
            Database = "my_analytics",
            Username = "admin",
            Password = "P@ssw0rd",
            ClientName = "LP.ClickHouse.Tests",
            Compress = false,
            CheckCompressedHash = true,
            CommandTimeoutSeconds = 120
        };

        options.Host.Should().Be("192.168.1.100");
        options.Port.Should().Be(9000);
        options.Protocol.Should().Be("https");
        options.Database.Should().Be("my_analytics");
        options.Username.Should().Be("admin");
        options.Password.Should().Be("P@ssw0rd");
        options.ClientName.Should().Be("LP.ClickHouse.Tests");
        options.Compress.Should().BeFalse();
        options.CheckCompressedHash.Should().BeTrue();
        options.CommandTimeoutSeconds.Should().Be(120);
    }
}
