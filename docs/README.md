# LP.ClickHouse - ClickHouse 分析示范项目

基于 .NET 8 的 ClickHouse 示例项目，参考 `LP.Milvus` 的项目组织方式，落地了 `E:\Documents\Learning\ClickHouse` 里的两个核心场景：
- 日志分析：错误日志、接口吞吐、P95 响应时间
- 行为分析：DAU、简单漏斗转化

教程文档另外补充了两类更贴近真实业务的用例：
- 准 CRUD：订单快照、版本更新、逻辑删除
- 复杂查询：多维聚合、留存分析、路径分析

## 项目结构

```text
LP.ClickHouse/
├── src/LP.ClickHouse.Core/
├── samples/LP.ClickHouse.Api/
├── tests/LP.ClickHouse.Tests/
├── docker/
└── docs/
```

## 关键设计
- `api_logs` 使用 `MergeTree`，按月分区，按时间 + 路径 + 状态码排序
- `user_events` 用于展示 `uniq` 和 `countIf` 的分析能力
- `api_logs` TTL 为 30 天，`user_events` TTL 为 90 天
- 数据落在容器 `/var/lib/clickhouse`，通过 Docker volume 持久化

## 快速开始

```bash
cd docker
docker compose up -d

cd ..\samples\LP.ClickHouse.Api
dotnet run
```

默认地址：`http://localhost:5066`

监控地址：
- Prometheus: `http://localhost:9090`
- Grafana: `http://localhost:3000`（admin/123456）
- ClickHouse Prometheus Metrics: `http://localhost:9363/metrics`

### 初始化表结构

```bash
curl -X POST http://localhost:5066/api/schema/init
```

### 写入演示数据

```bash
curl -X POST http://localhost:5066/api/demodata/seed -H "Content-Type: application/json" -d '{"logCount":5000,"eventCount":10000,"orderCount":2000}'
```

## API 端点
- `GET /api/schema/ping`
- `POST /api/schema/init`
- `POST /api/demodata/seed`
- `GET /api/analytics/recent-errors`
- `GET /api/analytics/api-stats`
- `GET /api/analytics/dau`
- `GET /api/analytics/funnel`
- `POST /api/orderdemo/create`
- `GET /api/orderdemo/latest`
- `GET /api/orderdemo/history`
- `POST /api/orderdemo/update-status`
- `POST /api/orderdemo/delete`
- `GET /api/advancedanalytics/slow-apis`
- `GET /api/advancedanalytics/retention`
- `GET /api/advancedanalytics/path-funnel`

## 监控栈

参考 `LP.Milvus` 的做法，这里也使用固定三件套：
- ClickHouse：暴露 Prometheus metrics endpoint
- Prometheus：定时抓取 ClickHouse 和自身指标
- Grafana：自动加载 datasource 和 dashboard

### 指标怎么流转
- ClickHouse 在 `docker/config.d/prometheus.xml` 中开启 `/metrics`
- Prometheus 按 `15s` 抓取 `clickhouse:9363/metrics`
- Grafana 默认连接 `Prometheus` datasource
- 仪表盘文件会在 Grafana 启动时自动加载

### 内置仪表盘
- Dashboard 名称：`ClickHouse / ClickHouse Overview`
- 重点展示：运行中查询数、HTTP/TCP 连接数、Query Throughput、Rows Processed、Uptime

### 仅启动监控组件

```bash
cd docker
docker compose up -d clickhouse prometheus grafana
```

## 下一步建议
- 增加物化视图做小时级预聚合
- 接真实业务日志源，替换演示数据生成器
- 增加维表查询，演示“先聚合再 JOIN”
- 把教程里的准 CRUD 和复杂查询补成可直接调用的示例 API



