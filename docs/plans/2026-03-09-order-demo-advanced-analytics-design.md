# 订单准 CRUD 与复杂查询设计

**日期：** 2026-03-09

**目标：** 为教程项目补充可直接调用的示例 API，覆盖订单快照准 CRUD 与复杂查询两类实战用例。

## 设计结论

- 保留现有 `Schema / Seed / Analytics` 三段式结构。
- 新增 `OrderDemoService` 负责订单快照的业务语义 CRUD。
- 新增 `AdvancedAnalyticsService` 负责慢接口排行、留存与路径漏斗查询。
- 扩展 `SchemaSqlBuilder` 与 `ClickHouseSchemaService`，新增 `order_snapshots` 表。
- 扩展 `ClickHouseSeedService`，支持订单快照造数，保证新接口开箱即用。
- API 层新增 `OrderDemoController` 与 `AdvancedAnalyticsController`，保持现有控制器职责单一。

## 数据模型

### order_snapshots
- `order_id`：订单 ID
- `user_id`：用户 ID
- `order_no`：订单号
- `status`：订单状态
- `pay_amount`：支付金额
- `city`：城市
- `created_at`：订单创建时间
- `updated_at`：快照更新时间
- `version`：快照版本号
- `is_deleted`：逻辑删除标记

**引擎：** `ReplacingMergeTree(version)`

## API 设计

### 订单准 CRUD
- `POST /api/orderdemo/create`
- `POST /api/orderdemo/update-status`
- `GET /api/orderdemo/latest`
- `GET /api/orderdemo/history`
- `POST /api/orderdemo/delete`

### 复杂查询
- `GET /api/advancedanalytics/slow-apis`
- `GET /api/advancedanalytics/retention`
- `GET /api/advancedanalytics/path-funnel`

## 测试策略

- 先补单测，再补实现。
- 扩展 `SchemaSqlBuilder` 相关测试，覆盖新建表 SQL。
- 扩展 `ServiceCollectionExtensionsTests`，覆盖新服务注册。
- 扩展 `EntitiesTests`，覆盖新增实体默认值与典型赋值。
- 最后执行 `dotnet build` 强制验证编译通过。
