using Xunit;
using FluentAssertions;
using LP.ClickHouse.Core.Builders;

namespace LP.ClickHouse.Tests;

public class SchemaSqlBuilderTests
{
    [Fact]
    public void BuildCreateApiLogsTableSql_ShouldContainMergeTreeAndTtl()
    {
        var sql = SchemaSqlBuilder.BuildCreateApiLogsTableSql("lp_observability");
        sql.Should().Contain("ENGINE = MergeTree()");
        sql.Should().Contain("TTL timestamp + INTERVAL 30 DAY");
        sql.Should().Contain("ORDER BY (timestamp, api_path, status_code)");
    }

    [Fact]
    public void BuildCreateOrderSnapshotsTableSql_ShouldContainReplacingMergeTreeAndTtl()
    {
        var sql = SchemaSqlBuilder.BuildCreateOrderSnapshotsTableSql("lp_observability");
        sql.Should().Contain("ENGINE = ReplacingMergeTree(version)");
        sql.Should().Contain("TTL created_at + INTERVAL 180 DAY");
        sql.Should().Contain("ORDER BY (order_id, updated_at)");
    }

    [Fact]
    public void SanitizeIdentifier_ShouldRejectUnsafeValue()
    {
        var action = () => SchemaSqlBuilder.SanitizeIdentifier("lp-observability");
        action.Should().Throw<ArgumentException>();
    }
}
