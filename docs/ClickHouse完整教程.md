# ClickHouse 完整教程

> 面向高级开发者/架构师的 ClickHouse 实战指南（.NET 技术栈）

---

## 目录

- [第一部分：基础入门](#第一部分基础入门)
- [第二部分：实战场景](#第二部分实战场景)
- [第三部分：性能优化](#第三部分性能优化)
- [第四部分：集群部署](#第四部分集群部署)

---

## 第一部分：基础入门

### 1. ClickHouse 是什么

#### 业务场景

你的 .NET 系统每天产生 10 亿条日志，需要实时查询：
- "过去 1 小时内，哪个 API 接口错误率最高？"
- "昨天访问量 TOP 10 的用户是谁？"
- "本月每天的活跃用户数趋势如何？"

用 Elasticsearch 查询这些数据：
- 简单聚合还行，但高基数字段（如 user_id）的 `terms` 聚合越来越慢
- 多维度交叉分析（按接口 × 状态码 × 时间粒度）DSL 写起来极其复杂
- 存储成本高——倒排索引 + doc_values + _source 三份数据，磁盘占用是原始数据的 3-5 倍
- 想做个同比环比？ES 没有窗口函数，只能应用层拼接

#### 问题

Elasticsearch 的设计目标是**全文搜索**，被拿来做日志分析是"能用但不够好"：
- 聚合查询基于 doc_values，数据量过亿后性能急剧下降
- DSL 语法复杂，一个多层嵌套聚合动辄几十行 JSON
- 不支持 JOIN、窗口函数、子查询等 SQL 分析能力
- 存储效率低，同样的数据 ES 占用空间是 ClickHouse 的 5-10 倍

#### 方案

ClickHouse 是一个**列式存储**的 OLAP 数据库，专为分析场景设计：
- 列式存储：只读取需要的列，减少 I/O
- 向量化执行：利用 CPU SIMD 指令加速计算
- 数据压缩：同一列数据类型相同，压缩比高（通常 10:1）
- 分布式查询：支持水平扩展，处理 PB 级数据

#### 性能对比

**类比**：查询"过去 7 天每天的订单总额"

- **Elasticsearch**：先把数据从 doc_values 列式结构中取出，通过 `date_histogram` + `sum` 聚合计算。看起来也是列式？但 ES 的聚合走的是 Lucene 的 doc_values，需要逐个 segment 遍历再合并，且无法利用 CPU SIMD 指令
- **ClickHouse**：直接读取"日期"和"金额"两列的压缩数据块，用向量化引擎批量计算，一次处理 8192 行

**原理**：

| 维度 | Elasticsearch | ClickHouse |
|------|--------------|------------|
| 存储模型 | 倒排索引 + doc_values + _source | 列式存储 + 稀疏索引 |
| 1 亿行聚合查询 | 3-10 秒（取决于基数） | 50-200 毫秒 |
| 10 亿行磁盘占用 | ~500 GB（3-5x 膨胀） | ~50 GB（10:1 压缩） |
| 多维分析 | DSL 嵌套聚合，复杂难维护 | 标准 SQL，GROUP BY 随便写 |
| JOIN 能力 | 几乎没有 | 支持多种 JOIN |

**代码示例**（.NET 客户端）：

```csharp
using ClickHouse.Client.ADO;
using ClickHouse.Client.Utility;

// 连接 ClickHouse
using var connection = new ClickHouseConnection("Host=localhost;Port=9000;Database=default");
await connection.OpenAsync();

// 查询过去 7 天每天的订单总额
var command = connection.CreateCommand();
command.CommandText = @"
    SELECT
        toDate(order_time) AS date,
        sum(amount) AS total_amount
    FROM orders
    WHERE order_time >= now() - INTERVAL 7 DAY
    GROUP BY date
    ORDER BY date";

using var reader = await command.ExecuteReaderAsync();
while (await reader.ReadAsync())
{
    var date = reader.GetDateTime(0);
    var amount = reader.GetDecimal(1);
    Console.WriteLine($"{date:yyyy-MM-dd}: {amount:C}");
}
// 查询时间：~50ms（1 亿行数据）
```


