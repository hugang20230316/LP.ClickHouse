using Xunit;
using FluentAssertions;
using LP.ClickHouse.Core.Entities;

namespace LP.ClickHouse.Tests;

public class EntitiesTests
{
    [Fact]
    public void ApiLogRecord_DefaultValues_ShouldBeInitialized()
    {
        var record = new ApiLogRecord();

        record.LogId.Should().Be(Guid.Empty);
        record.Timestamp.Should().Be(default(DateTime));
        record.Level.Should().BeEmpty();
        record.ApiPath.Should().BeEmpty();
        record.Method.Should().BeEmpty();
        record.StatusCode.Should().Be(0);
        record.ResponseTimeMs.Should().Be(0);
        record.UserId.Should().Be(0);
        record.IpAddress.Should().BeEmpty();
        record.ErrorMessage.Should().BeEmpty();
        record.TraceId.Should().BeEmpty();
    }

    [Fact]
    public void ApiLogRecord_ShouldAcceptTypicalValues()
    {
        var now = DateTime.UtcNow;
        var logId = Guid.NewGuid();

        var record = new ApiLogRecord
        {
            LogId = logId,
            Timestamp = now,
            Level = "Error",
            ApiPath = "/api/orders",
            Method = "POST",
            StatusCode = 500,
            ResponseTimeMs = 1200,
            UserId = 42,
            IpAddress = "10.0.0.1",
            ErrorMessage = "upstream timeout",
            TraceId = "abc123"
        };

        record.LogId.Should().Be(logId);
        record.Timestamp.Should().Be(now);
        record.StatusCode.Should().Be(500);
        record.ResponseTimeMs.Should().Be(1200);
        record.UserId.Should().Be(42);
    }

    [Fact]
    public void ApiEndpointStats_DefaultValues_ShouldBeInitialized()
    {
        var stats = new ApiEndpointStats();

        stats.BucketStart.Should().Be(default(DateTime));
        stats.ApiPath.Should().BeEmpty();
        stats.RequestCount.Should().Be(0);
        stats.ErrorCount.Should().Be(0);
        stats.AvgResponseTimeMs.Should().Be(0);
        stats.P95ResponseTimeMs.Should().Be(0);
    }

    [Fact]
    public void DailyActiveUserMetric_DefaultValues_ShouldBeInitialized()
    {
        var metric = new DailyActiveUserMetric();

        metric.ActivityDate.Should().Be(default(DateTime));
        metric.ActiveUsers.Should().Be(0);
    }

    [Fact]
    public void FunnelMetric_DefaultValues_ShouldBeInitialized()
    {
        var funnel = new FunnelMetric();

        funnel.ViewedCount.Should().Be(0);
        funnel.ClickedCount.Should().Be(0);
        funnel.CompletedCount.Should().Be(0);
        funnel.ViewToClickRate.Should().Be(0);
        funnel.ClickToCompletionRate.Should().Be(0);
    }

    [Fact]
    public void FunnelMetric_ShouldAcceptRealisticRates()
    {
        var funnel = new FunnelMetric
        {
            ViewedCount = 10000,
            ClickedCount = 6200,
            CompletedCount = 1922,
            ViewToClickRate = 62.0,
            ClickToCompletionRate = 31.0
        };

        funnel.ViewedCount.Should().Be(10000);
        funnel.ViewToClickRate.Should().BeApproximately(62.0, 0.01);
        funnel.ClickToCompletionRate.Should().BeApproximately(31.0, 0.01);
    }

    [Fact]
    public void OrderSnapshotRecord_DefaultValues_ShouldBeInitialized()
    {
        var record = new OrderSnapshotRecord();

        record.OrderId.Should().Be(0);
        record.UserId.Should().Be(0);
        record.OrderNo.Should().BeEmpty();
        record.Status.Should().BeEmpty();
        record.PayAmount.Should().Be(0);
        record.City.Should().BeEmpty();
        record.CreatedAt.Should().Be(default(DateTime));
        record.UpdatedAt.Should().Be(default(DateTime));
        record.Version.Should().Be(0);
        record.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void OrderSnapshotRecord_ShouldAcceptTypicalValues()
    {
        var now = DateTime.UtcNow;
        var record = new OrderSnapshotRecord
        {
            OrderId = 202603090001,
            UserId = 10086,
            OrderNo = "NO202603090001",
            Status = "Paid",
            PayAmount = 199.00m,
            City = "上海",
            CreatedAt = now.AddMinutes(-5),
            UpdatedAt = now,
            Version = 2,
            IsDeleted = false
        };

        record.OrderId.Should().Be(202603090001);
        record.PayAmount.Should().Be(199.00m);
        record.Version.Should().Be(2);
        record.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void SlowApiMetric_DefaultValues_ShouldBeInitialized()
    {
        var metric = new SlowApiMetric();

        metric.ApiPath.Should().BeEmpty();
        metric.Method.Should().BeEmpty();
        metric.RequestCount.Should().Be(0);
        metric.ErrorCount.Should().Be(0);
        metric.ErrorRate.Should().Be(0);
        metric.AvgResponseTimeMs.Should().Be(0);
        metric.P95ResponseTimeMs.Should().Be(0);
    }

    [Fact]
    public void SlowApiMetric_ShouldAcceptTypicalValues()
    {
        var metric = new SlowApiMetric
        {
            ApiPath = "/api/orders",
            Method = "POST",
            RequestCount = 3200,
            ErrorCount = 64,
            ErrorRate = 2.0,
            AvgResponseTimeMs = 180.5,
            P95ResponseTimeMs = 820.0
        };

        metric.RequestCount.Should().Be(3200);
        metric.ErrorRate.Should().Be(2.0);
        metric.P95ResponseTimeMs.Should().Be(820.0);
    }

    [Fact]
    public void RetentionMetric_DefaultValues_ShouldBeInitialized()
    {
        var metric = new RetentionMetric();

        metric.CohortDate.Should().Be(default(DateTime));
        metric.DayOffset.Should().Be(0);
        metric.RetainedUsers.Should().Be(0);
        metric.RetentionRate.Should().Be(0);
    }

    [Fact]
    public void RetentionMetric_ShouldAcceptTypicalValues()
    {
        var metric = new RetentionMetric
        {
            CohortDate = new DateTime(2026, 3, 1),
            DayOffset = 3,
            RetainedUsers = 256,
            RetentionRate = 42.67
        };

        metric.DayOffset.Should().Be(3);
        metric.RetainedUsers.Should().Be(256);
        metric.RetentionRate.Should().BeApproximately(42.67, 0.01);
    }

    [Fact]
    public void PathFunnelMetric_DefaultValues_ShouldBeInitialized()
    {
        var metric = new PathFunnelMetric();

        metric.Level.Should().Be(0);
        metric.StepName.Should().BeEmpty();
        metric.UserCount.Should().Be(0);
    }

    [Fact]
    public void PathFunnelMetric_ShouldAcceptTypicalValues()
    {
        var metric = new PathFunnelMetric
        {
            Level = 3,
            StepName = "完成交易",
            UserCount = 1880
        };

        metric.Level.Should().Be(3);
        metric.StepName.Should().Be("完成交易");
        metric.UserCount.Should().Be(1880);
    }

    [Fact]
    public void SeedSummary_DefaultValues_ShouldBeInitialized()
    {
        var summary = new SeedSummary();

        summary.InsertedLogRows.Should().Be(0);
        summary.InsertedEventRows.Should().Be(0);
        summary.InsertedOrderRows.Should().Be(0);
        summary.GeneratedAtUtc.Should().Be(default(DateTime));
    }

    [Fact]
    public void SeedSummary_ShouldReflectInsertedCounts()
    {
        var now = DateTime.UtcNow;
        var summary = new SeedSummary
        {
            InsertedLogRows = 5000,
            InsertedEventRows = 15000,
            InsertedOrderRows = 3200,
            GeneratedAtUtc = now
        };

        summary.InsertedLogRows.Should().Be(5000);
        summary.InsertedEventRows.Should().Be(15000);
        summary.InsertedOrderRows.Should().Be(3200);
        summary.GeneratedAtUtc.Should().BeCloseTo(now, TimeSpan.FromSeconds(1));
    }
}
