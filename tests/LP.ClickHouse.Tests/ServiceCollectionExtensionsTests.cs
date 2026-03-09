using Xunit;
using FluentAssertions;
using ClickHouse.Driver;
using LP.ClickHouse.Core.Extensions;
using LP.ClickHouse.Core.Options;
using LP.ClickHouse.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LP.ClickHouse.Tests;

public class ServiceCollectionExtensionsTests
{
    private static IServiceProvider BuildServiceProvider(Dictionary<string, string?>? overrides = null)
    {
        var configData = new Dictionary<string, string?>
        {
            ["ClickHouse:Host"] = "test-host",
            ["ClickHouse:Port"] = "8123",
            ["ClickHouse:Protocol"] = "http",
            ["ClickHouse:Database"] = "test_db",
            ["ClickHouse:Username"] = "default",
            ["ClickHouse:Password"] = "",
            ["ClickHouse:Compress"] = "true",
            ["ClickHouse:CheckCompressedHash"] = "false",
            ["ClickHouse:CommandTimeoutSeconds"] = "60"
        };

        if (overrides != null)
        {
            foreach (var kv in overrides)
                configData[kv.Key] = kv.Value;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        var services = new ServiceCollection();
        services.AddClickHouseServices(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddClickHouseServices_ShouldBindOptions()
    {
        var sp = BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<ClickHouseOptions>>().Value;

        options.Host.Should().Be("test-host");
        options.Port.Should().Be(8123);
        options.Database.Should().Be("test_db");
        options.CommandTimeoutSeconds.Should().Be(60);
    }

    [Fact]
    public void AddClickHouseServices_ShouldRegisterClickHouseClient()
    {
        var sp = BuildServiceProvider();
        var client = sp.GetService<ClickHouseClient>();
        client.Should().NotBeNull();
    }

    [Fact]
    public void AddClickHouseServices_ShouldRegisterSchemaService()
    {
        var sp = BuildServiceProvider();
        var service = sp.GetService<IClickHouseSchemaService>();
        service.Should().NotBeNull();
        service.Should().BeOfType<ClickHouseSchemaService>();
    }

    [Fact]
    public void AddClickHouseServices_ShouldRegisterSeedService()
    {
        var sp = BuildServiceProvider();
        var service = sp.GetService<IClickHouseSeedService>();
        service.Should().NotBeNull();
        service.Should().BeOfType<ClickHouseSeedService>();
    }

    [Fact]
    public void AddClickHouseServices_ShouldRegisterAnalyticsService()
    {
        var sp = BuildServiceProvider();
        var service = sp.GetService<ILogAnalyticsService>();
        service.Should().NotBeNull();
        service.Should().BeOfType<LogAnalyticsService>();
    }

    [Fact]
    public void AddClickHouseServices_ShouldRegisterOrderDemoService()
    {
        var sp = BuildServiceProvider();
        var service = sp.GetService<IOrderDemoService>();
        service.Should().NotBeNull();
        service.Should().BeOfType<OrderDemoService>();
    }

    [Fact]
    public void AddClickHouseServices_ShouldRegisterAdvancedAnalyticsService()
    {
        var sp = BuildServiceProvider();
        var service = sp.GetService<IAdvancedAnalyticsService>();
        service.Should().NotBeNull();
        service.Should().BeOfType<AdvancedAnalyticsService>();
    }

    [Fact]
    public void AddClickHouseServices_ClientShouldBeSingleton()
    {
        var sp = BuildServiceProvider();
        var client1 = sp.GetRequiredService<ClickHouseClient>();
        var client2 = sp.GetRequiredService<ClickHouseClient>();
        client1.Should().BeSameAs(client2);
    }

    [Fact]
    public void AddClickHouseServices_ServicesShouldBeSingleton()
    {
        var sp = BuildServiceProvider();
        var schema1 = sp.GetRequiredService<IClickHouseSchemaService>();
        var schema2 = sp.GetRequiredService<IClickHouseSchemaService>();
        schema1.Should().BeSameAs(schema2);

        var seed1 = sp.GetRequiredService<IClickHouseSeedService>();
        var seed2 = sp.GetRequiredService<IClickHouseSeedService>();
        seed1.Should().BeSameAs(seed2);

        var analytics1 = sp.GetRequiredService<ILogAnalyticsService>();
        var analytics2 = sp.GetRequiredService<ILogAnalyticsService>();
        analytics1.Should().BeSameAs(analytics2);

        var orderDemo1 = sp.GetRequiredService<IOrderDemoService>();
        var orderDemo2 = sp.GetRequiredService<IOrderDemoService>();
        orderDemo1.Should().BeSameAs(orderDemo2);

        var advancedAnalytics1 = sp.GetRequiredService<IAdvancedAnalyticsService>();
        var advancedAnalytics2 = sp.GetRequiredService<IAdvancedAnalyticsService>();
        advancedAnalytics1.Should().BeSameAs(advancedAnalytics2);
    }

    [Fact]
    public void AddClickHouseServices_WithCustomConfig_ShouldReflectInOptions()
    {
        var sp = BuildServiceProvider(new Dictionary<string, string?>
        {
            ["ClickHouse:Host"] = "192.168.1.100",
            ["ClickHouse:Port"] = "9000",
            ["ClickHouse:Database"] = "custom_db",
            ["ClickHouse:Password"] = "my_password"
        });

        var options = sp.GetRequiredService<IOptions<ClickHouseOptions>>().Value;

        options.Host.Should().Be("192.168.1.100");
        options.Port.Should().Be(9000);
        options.Database.Should().Be("custom_db");
        options.Password.Should().Be("my_password");
    }
}
