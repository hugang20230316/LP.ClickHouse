# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 项目概述

ClickHouse SDK 封装库（C# / .NET 8），提供 API 日志和用户事件的写入、查询、分析能力。包含核心类库、示例 Web API、单元测试和 Docker 监控栈。

## 常用命令

```bash
# 构建
dotnet build LP.ClickHouse.slnx

# 测试（全部）
dotnet test LP.ClickHouse.slnx

# 运行单个测试
dotnet test tests/LP.ClickHouse.Tests --filter "FullyQualifiedName~ConnectionStringBuilderTests"

# 启动 ClickHouse + 监控栈
cd docker && docker compose up -d

# 运行示例 API（默认 http://localhost:5066）
dotnet run --project samples/LP.ClickHouse.Api

# 初始化表结构 + 写入演示数据
curl -X POST http://localhost:5066/api/schema/init
curl -X POST http://localhost:5066/api/demodata/seed -H "Content-Type: application/json" -d '{"logCount":5000,"eventCount":10000}'
```

## 架构

```
src/LP.ClickHouse.Core/          # 核心类库（纯 SDK，不依赖 ASP.NET Core）
  Options/                        # ClickHouseOptions — 强类型配置，绑定 "ClickHouse" 配置节
  Utilities/                      # 连接字符串构建器
  Builders/                       # SchemaSqlBuilder — DDL 生成，含标识符白名单校验
  Entities/                       # ApiLogRecord, UserEventRecord, FunnelMetric
  Services/                       # 接口 + 实现（Schema/Seed/Analytics）
  Extensions/                     # AddClickHouseServices() — 一次性 DI 注册

samples/LP.ClickHouse.Api/       # Minimal Hosting Web API，Controller 为薄壳
tests/LP.ClickHouse.Tests/       # xUnit + FluentAssertions
docker/                           # ClickHouse + Prometheus + Grafana（admin/123456）
```

**DI 生命周期**：`ClickHouseClient` 和三个 Service 均为 Singleton（无状态）。

**ClickHouse 连接**：HTTP 协议（端口 8123），数据库 `lp_observability`，写入用 `InsertBinaryAsync`，查询用 `ExecuteReaderAsync`。

**表设计**：`api_logs`（MergeTree，按月分区，TTL 30 天）和 `user_events`（TTL 90 天），排序键针对时间范围查询优化。

## 代码约定

- file-scoped namespace（`namespace X;`）
- 中文 XML 文档注释和代码注释
- 包版本通过 `Directory.Build.props` 集中管理，csproj 用 `$(ClickHouseDriverVersion)` 引用
- 控制器用表达式体方法，业务逻辑全在 Core Service 中
- SQL 标识符用 `SanitizeIdentifier()` 正则白名单校验

## 协作约定

- 执行预计超过 30 秒、且终端不会持续输出的命令前，先说明当前要跑的动作、目的、预计耗时，以及成功或失败会看什么信号。
- 长命令执行期间，至少每 30-60 秒同步一次当前状态；如果还在等待，也要明确说明是在等待哪一步返回，而不是让用户无反馈空等。
- 涉及 harness、模型调用、构建或测试整链路时，默认先做 30 秒级短验证；短验证通过后，再升级到完整链路。
- 如果长命令超过 2 分钟仍没有明确进展信号，优先停止继续硬等，先向用户汇报当前卡点、已确认的信息和下一步缩小范围的方案。
