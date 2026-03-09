# 订单准 CRUD 与复杂查询 Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 为示例项目补充订单快照准 CRUD 与复杂查询 API，并同步建表、造数与测试。

**Architecture:** 继续沿用现有 `Schema / Seed / Analytics` 三段式结构。新增 `OrderDemoService` 处理订单快照写入与查询，新增 `AdvancedAnalyticsService` 处理复杂分析查询；`SchemaService` 和 `SeedService` 只做必要扩展，避免职责混杂。

**Tech Stack:** .NET 8、ASP.NET Core Controllers、ClickHouse.Driver、xUnit、FluentAssertions

---

### Task 1: 补失败测试

**Files:**
- Modify: `tests/LP.ClickHouse.Tests/SchemaSqlBuilderTests.cs`
- Modify: `tests/LP.ClickHouse.Tests/SchemaSqlBuilderFullTests.cs`
- Modify: `tests/LP.ClickHouse.Tests/ServiceCollectionExtensionsTests.cs`
- Modify: `tests/LP.ClickHouse.Tests/EntitiesTests.cs`

**Step 1: Write the failing test**
- 为 `BuildCreateOrderSnapshotsTableSql` 增加断言。
- 为 `OrderSnapshotRecord`、`SlowApiMetric`、`RetentionMetric`、`PathFunnelMetric` 增加默认值与典型赋值断言。
- 为 DI 注册增加 `IOrderDemoService` 与 `IAdvancedAnalyticsService` 的断言。

**Step 2: Run test to verify it fails**
- Run: `dotnet test tests/LP.ClickHouse.Tests/LP.ClickHouse.Tests.csproj -v minimal`
- Expected: FAIL，提示缺少方法、类型或服务注册。

### Task 2: 扩展核心模型与建表

**Files:**
- Modify: `src/LP.ClickHouse.Core/Builders/SchemaSqlBuilder.cs`
- Modify: `src/LP.ClickHouse.Core/Services/ClickHouseSchemaService.cs`
- Create: `src/LP.ClickHouse.Core/Entities/OrderSnapshotRecord.cs`
- Create: `src/LP.ClickHouse.Core/Entities/SlowApiMetric.cs`
- Create: `src/LP.ClickHouse.Core/Entities/RetentionMetric.cs`
- Create: `src/LP.ClickHouse.Core/Entities/PathFunnelMetric.cs`

**Step 1: Write minimal implementation**
- 新增 `BuildCreateOrderSnapshotsTableSql`。
- 在初始化流程里加上 `order_snapshots` 建表。
- 添加 4 个实体类型。

**Step 2: Run tests**
- Run: `dotnet test tests/LP.ClickHouse.Tests/LP.ClickHouse.Tests.csproj -v minimal`
- Expected: 仍有失败，指向服务与控制器尚未实现。

### Task 3: 扩展造数与新增服务

**Files:**
- Modify: `src/LP.ClickHouse.Core/Services/IClickHouseSeedService.cs`
- Modify: `src/LP.ClickHouse.Core/Services/ClickHouseSeedService.cs`
- Create: `src/LP.ClickHouse.Core/Services/IOrderDemoService.cs`
- Create: `src/LP.ClickHouse.Core/Services/OrderDemoService.cs`
- Create: `src/LP.ClickHouse.Core/Services/IAdvancedAnalyticsService.cs`
- Create: `src/LP.ClickHouse.Core/Services/AdvancedAnalyticsService.cs`
- Modify: `src/LP.ClickHouse.Core/Extensions/ServiceCollectionExtensions.cs`

**Step 1: Write minimal implementation**
- `GenerateAsync` 增加订单快照造数参数。
- 新增订单准 CRUD 服务方法。
- 新增复杂查询服务方法。
- 注册新服务。

**Step 2: Run tests**
- Run: `dotnet test tests/LP.ClickHouse.Tests/LP.ClickHouse.Tests.csproj -v minimal`
- Expected: 只剩 API 模型或控制器相关失败。

### Task 4: 新增 API 控制器与模型

**Files:**
- Modify: `samples/LP.ClickHouse.Api/Models/ApiModels.cs`
- Create: `samples/LP.ClickHouse.Api/Models/OrderDemoModels.cs`
- Create: `samples/LP.ClickHouse.Api/Controllers/OrderDemoController.cs`
- Create: `samples/LP.ClickHouse.Api/Controllers/AdvancedAnalyticsController.cs`
- Modify: `samples/LP.ClickHouse.Api/Controllers/DemoDataController.cs`

**Step 1: Write minimal implementation**
- 扩展 `SeedRequest` 增加 `OrderCount`。
- 新增订单相关请求模型。
- 新增两个控制器，暴露教程对应接口。

**Step 2: Run build**
- Run: `dotnet build e:\Projects\LP\LP.ClickHouse\LP.ClickHouse.slnx -v minimal`
- Expected: PASS

### Task 5: 回归验证

**Files:**
- Review: `docs/ClickHouse教程-Part2-实战场景.md`
- Review: `docs/README.md`

**Step 1: Run full validation**
- Run: `dotnet test tests/LP.ClickHouse.Tests/LP.ClickHouse.Tests.csproj -v minimal`
- Run: `dotnet build e:\Projects\LP\LP.ClickHouse\LP.ClickHouse.slnx -v minimal`

**Step 2: Review changed files**
- 确认只改教程相关 API、建表、造数和测试。
