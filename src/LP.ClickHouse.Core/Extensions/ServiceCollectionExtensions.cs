using ClickHouse.Driver;
using LP.ClickHouse.Core.Options;
using LP.ClickHouse.Core.Services;
using LP.ClickHouse.Core.Utilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LP.ClickHouse.Core.Extensions;

/// <summary>
/// 注册示例项目使用的 ClickHouse 客户端及配套服务。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 绑定配置、创建共享客户端，并注册建表、造数和分析服务。
    /// </summary>
    /// <param name="services">当前应用的依赖注入服务集合。</param>
    /// <param name="configuration">用于读取 ClickHouse 配置节的配置对象。</param>
    /// <returns>返回同一个服务集合，便于继续链式注册。</returns>
    public static IServiceCollection AddClickHouseServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ClickHouseOptions>(configuration.GetSection(ClickHouseOptions.SectionName));

        services.AddSingleton(sp => new ClickHouseClient(ClickHouseConnectionStringBuilder.Build(sp.GetRequiredService<IOptions<ClickHouseOptions>>().Value)));

        services.AddSingleton<IClickHouseSchemaService, ClickHouseSchemaService>();
        services.AddSingleton<IClickHouseSeedService, ClickHouseSeedService>();
        services.AddSingleton<ILogAnalyticsService, LogAnalyticsService>();
        services.AddSingleton<IOrderDemoService, OrderDemoService>();
        services.AddSingleton<IAdvancedAnalyticsService, AdvancedAnalyticsService>();
        return services;
    }
}
