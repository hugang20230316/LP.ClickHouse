using System.Net.Http;
using System.Net.Sockets;
using LP.ClickHouse.Core.Extensions;
using LP.ClickHouse.Core.Options;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;

// 配置 ASP.NET Core 主机，并注册示例 API 需要用到的 ClickHouse 相关服务。
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddClickHouseServices(builder.Configuration);
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddProblemDetails();
// 生成 Swagger 文档和调试页面，便于直接查看当前示例 API 的接口契约。
builder.Services.AddSwaggerGen(options =>
{
    // 注册单个 v1 文档，让所有控制器路由都归到同一份示例文档里。
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "LP.ClickHouse API",
        Version = "v1",
        Description = "LP.ClickHouse 示例项目的接口文档。"
    });
});

var app = builder.Build();

// 将 ClickHouse 不可用这类基础设施错误统一转换为 503，避免把驱动堆栈直接暴露给调用方。
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var problem = BuildProblemDetails(context, exception);

        context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problem);
    });
});

// 暴露 Swagger JSON 和 UI，便于本地联调时直接浏览所有控制器接口。
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    // 将 UI 绑定到当前生成的 v1 文档，避免打开页面后没有可浏览的接口定义。
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "LP.ClickHouse API v1");
});

// 提供一个最轻量的根路由，便于本地调试或容器启动后快速探活。
app.MapGet("/", () => Results.Ok(new { message = "LP.ClickHouse API is running", utcNow = DateTime.UtcNow }));

// 挂载控制器路由，暴露建表、造数和分析接口。
app.MapControllers();
app.Run();

static ProblemDetails BuildProblemDetails(HttpContext context, Exception? exception)
{
    if (exception is HttpRequestException || exception is SocketException || exception?.InnerException is SocketException)
    {
        var options = context.RequestServices.GetRequiredService<IOptions<ClickHouseOptions>>().Value;
        return new ProblemDetails
        {
            Status = StatusCodes.Status503ServiceUnavailable,
            Title = "ClickHouse 服务不可用",
            Detail = $"当前无法连接到 ClickHouse（{options.Protocol}://{options.Host}:{options.Port}）。请先启动 ClickHouse，或检查 ClickHouse 配置节。",
            Instance = context.Request.Path
        };
    }

    return new ProblemDetails
    {
        Status = StatusCodes.Status500InternalServerError,
        Title = "服务器内部错误",
        Detail = "处理请求时发生未预期错误，请查看应用日志。",
        Instance = context.Request.Path
    };
}
