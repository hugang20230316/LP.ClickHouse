using Xunit;
using FluentAssertions;
using LP.ClickHouse.Core.Builders;

namespace LP.ClickHouse.Tests;

public class SchemaSqlBuilderFullTests
{
    [Theory]
    [InlineData("my_database")]
    [InlineData("lp_observability")]
    [InlineData("DB01")]
    [InlineData("a")]
    [InlineData("A_B_C_123")]
    public void SanitizeIdentifier_ValidNames_ShouldReturnSameValue(string identifier)
    {
        var result = SchemaSqlBuilder.SanitizeIdentifier(identifier);
        result.Should().Be(identifier);
    }

    [Theory]
    [InlineData("lp-observability")]
    [InlineData("my database")]
    [InlineData("db;DROP TABLE")]
    [InlineData("name'OR'1'='1")]
    [InlineData("table.name")]
    [InlineData("db\"name")]
    [InlineData("名字")]
    public void SanitizeIdentifier_InvalidNames_ShouldThrow(string identifier)
    {
        var action = () => SchemaSqlBuilder.SanitizeIdentifier(identifier);
        action.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void SanitizeIdentifier_NullOrWhitespace_ShouldThrow(string? identifier)
    {
        var action = () => SchemaSqlBuilder.SanitizeIdentifier(identifier!);
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BuildCreateDatabaseSql_ShouldContainCreateDatabaseIfNotExists()
    {
        var sql = SchemaSqlBuilder.BuildCreateDatabaseSql("lp_observability");

        sql.Should().Contain("CREATE DATABASE IF NOT EXISTS");
        sql.Should().Contain("lp_observability");
    }

    [Fact]
    public void BuildCreateDatabaseSql_WithUnsafeName_ShouldThrow()
    {
        var action = () => SchemaSqlBuilder.BuildCreateDatabaseSql("db;DROP TABLE users");
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BuildCreateApiLogsTableSql_ShouldContainAllColumns()
    {
        var sql = SchemaSqlBuilder.BuildCreateApiLogsTableSql("test_db");

        sql.Should().Contain("log_id UUID");
        sql.Should().Contain("timestamp DateTime64(3, 'UTC')");
        sql.Should().Contain("level LowCardinality(String)");
        sql.Should().Contain("api_path LowCardinality(String)");
        sql.Should().Contain("method LowCardinality(String)");
        sql.Should().Contain("status_code UInt16");
        sql.Should().Contain("response_time_ms UInt32");
        sql.Should().Contain("user_id UInt64");
        sql.Should().Contain("ip_address String");
        sql.Should().Contain("error_message String");
        sql.Should().Contain("trace_id String");
    }

    [Fact]
    public void BuildCreateApiLogsTableSql_ShouldContainEngineAndPartition()
    {
        var sql = SchemaSqlBuilder.BuildCreateApiLogsTableSql("test_db");

        sql.Should().Contain("ENGINE = MergeTree()");
        sql.Should().Contain("PARTITION BY toYYYYMM(timestamp)");
        sql.Should().Contain("ORDER BY (timestamp, api_path, status_code)");
        sql.Should().Contain("SETTINGS index_granularity = 8192");
    }

    [Fact]
    public void BuildCreateApiLogsTableSql_ShouldHave30DayTtl()
    {
        var sql = SchemaSqlBuilder.BuildCreateApiLogsTableSql("test_db");
        sql.Should().Contain("TTL timestamp + INTERVAL 30 DAY");
    }

    [Fact]
    public void BuildCreateApiLogsTableSql_ShouldUseCorrectDatabasePrefix()
    {
        var sql = SchemaSqlBuilder.BuildCreateApiLogsTableSql("my_analytics");
        sql.Should().Contain("my_analytics.api_logs");
        sql.Should().NotContain("lp_observability");
    }

    [Fact]
    public void BuildCreateApiLogsTableSql_ShouldContainCreateTableIfNotExists()
    {
        var sql = SchemaSqlBuilder.BuildCreateApiLogsTableSql("test_db");
        sql.Should().Contain("CREATE TABLE IF NOT EXISTS");
    }

    [Fact]
    public void BuildCreateUserEventsTableSql_ShouldContainAllColumns()
    {
        var sql = SchemaSqlBuilder.BuildCreateUserEventsTableSql("test_db");

        sql.Should().Contain("event_time DateTime64(3, 'UTC')");
        sql.Should().Contain("user_id UInt64");
        sql.Should().Contain("session_id String");
        sql.Should().Contain("event_type LowCardinality(String)");
        sql.Should().Contain("page LowCardinality(String)");
        sql.Should().Contain("device LowCardinality(String)");
        sql.Should().Contain("trace_id String");
    }

    [Fact]
    public void BuildCreateUserEventsTableSql_ShouldContainEngineAndPartition()
    {
        var sql = SchemaSqlBuilder.BuildCreateUserEventsTableSql("test_db");

        sql.Should().Contain("ENGINE = MergeTree()");
        sql.Should().Contain("PARTITION BY toYYYYMM(event_time)");
        sql.Should().Contain("ORDER BY (event_time, event_type, user_id)");
        sql.Should().Contain("SETTINGS index_granularity = 8192");
    }

    [Fact]
    public void BuildCreateUserEventsTableSql_ShouldHave90DayTtl()
    {
        var sql = SchemaSqlBuilder.BuildCreateUserEventsTableSql("test_db");
        sql.Should().Contain("TTL event_time + INTERVAL 90 DAY");
    }

    [Fact]
    public void BuildCreateUserEventsTableSql_ShouldUseCorrectDatabasePrefix()
    {
        var sql = SchemaSqlBuilder.BuildCreateUserEventsTableSql("event_store");
        sql.Should().Contain("event_store.user_events");
    }

    [Fact]
    public void BuildCreateUserEventsTableSql_ShouldContainCreateTableIfNotExists()
    {
        var sql = SchemaSqlBuilder.BuildCreateUserEventsTableSql("test_db");
        sql.Should().Contain("CREATE TABLE IF NOT EXISTS");
    }

    [Fact]
    public void BuildCreateOrderSnapshotsTableSql_ShouldContainAllColumns()
    {
        var sql = SchemaSqlBuilder.BuildCreateOrderSnapshotsTableSql("test_db");

        sql.Should().Contain("order_id UInt64");
        sql.Should().Contain("user_id UInt64");
        sql.Should().Contain("order_no String");
        sql.Should().Contain("status LowCardinality(String)");
        sql.Should().Contain("pay_amount Decimal(18, 2)");
        sql.Should().Contain("city LowCardinality(String)");
        sql.Should().Contain("created_at DateTime64(3, 'UTC')");
        sql.Should().Contain("updated_at DateTime64(3, 'UTC')");
        sql.Should().Contain("version UInt64");
        sql.Should().Contain("is_deleted UInt8");
    }

    [Fact]
    public void BuildCreateOrderSnapshotsTableSql_ShouldContainEngineAndPartition()
    {
        var sql = SchemaSqlBuilder.BuildCreateOrderSnapshotsTableSql("test_db");

        sql.Should().Contain("ENGINE = ReplacingMergeTree(version)");
        sql.Should().Contain("PARTITION BY toYYYYMM(created_at)");
        sql.Should().Contain("ORDER BY (order_id, updated_at)");
        sql.Should().Contain("SETTINGS index_granularity = 8192");
    }

    [Fact]
    public void BuildCreateOrderSnapshotsTableSql_ShouldHave180DayTtl()
    {
        var sql = SchemaSqlBuilder.BuildCreateOrderSnapshotsTableSql("test_db");
        sql.Should().Contain("TTL created_at + INTERVAL 180 DAY");
    }

    [Fact]
    public void BuildCreateOrderSnapshotsTableSql_ShouldUseCorrectDatabasePrefix()
    {
        var sql = SchemaSqlBuilder.BuildCreateOrderSnapshotsTableSql("order_store");
        sql.Should().Contain("order_store.order_snapshots");
    }

    [Fact]
    public void BuildCreateOrderSnapshotsTableSql_ShouldContainCreateTableIfNotExists()
    {
        var sql = SchemaSqlBuilder.BuildCreateOrderSnapshotsTableSql("test_db");
        sql.Should().Contain("CREATE TABLE IF NOT EXISTS");
    }
}
