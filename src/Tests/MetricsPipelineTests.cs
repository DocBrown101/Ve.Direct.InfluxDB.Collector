namespace Tests;

using System.Collections.Concurrent;
using InfluxDB.Client.Writes;
using Ve.Direct.InfluxDB.Collector.Metrics;
using Xunit;

public sealed class MetricsPipelineTests
{
    [Fact]
    public void CreatePayload_TwoDevices_ProducesSeparateDeviceSeries()
    {
        var first = MetricsCompositor.ComposeMetrics(
            new Dictionary<string, string> { ["SER#"] = "HQ111", ["V"] = "13000" },
            false);
        var second = MetricsCompositor.ComposeMetrics(
            new Dictionary<string, string> { ["SER#"] = "HQ222", ["V"] = "12500" },
            false);
        var timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        using var client = new PayloadClient("ve_direct", "collector", (_, _) => Task.CompletedTask);
        var firstLines = client.CreatePayload(first, timestamp).Select(ToLineProtocol).ToArray();
        var secondLines = client.CreatePayload(second, timestamp).Select(ToLineProtocol).ToArray();

        Assert.Equal(13000, first.BatteryVoltageMillivolts);
        Assert.Equal(12500, second.BatteryVoltageMillivolts);
        Assert.All(firstLines, line => Assert.Contains("device=HQ111", line, StringComparison.Ordinal));
        Assert.All(secondLines, line => Assert.Contains("device=HQ222", line, StringComparison.Ordinal));
        Assert.Contains(firstLines, line => line.StartsWith("ve_direct_battery,", StringComparison.Ordinal)
            && line.Contains("voltage=13000i", StringComparison.Ordinal));
        Assert.Contains(secondLines, line => line.StartsWith("ve_direct_battery,", StringComparison.Ordinal)
            && line.Contains("voltage=12500i", StringComparison.Ordinal));
    }

    [Fact]
    public void ComposeMetrics_MissingSerial_Throws()
    {
        var data = new Dictionary<string, string> { ["V"] = "13000" };

        Assert.Throws<ArgumentException>(() => MetricsCompositor.ComposeMetrics(data, false));
    }

    [Fact]
    public void ComposeMetrics_KnownFields_MapsValuesAndUnits()
    {
        var frame = new Dictionary<string, string>
        {
            ["SER#"] = "HQ111",
            ["V"] = "13000",
            ["I"] = "500",
            ["VPV"] = "40000",
            ["PPV"] = "200",
            ["IL"] = "250",
            ["H20"] = "12",
            ["H21"] = "210",
            ["CS"] = "3",
            ["ERR"] = "0",
            ["MPPT"] = "2",
            ["LOAD"] = "ON"
        };

        var metrics = MetricsCompositor.ComposeMetrics(frame, false);

        Assert.Equal("HQ111", metrics.SerialNumber);
        Assert.Equal(13000, metrics.BatteryVoltageMillivolts);
        Assert.Equal(500, metrics.BatteryCurrentMilliamps);
        Assert.Equal(40000, metrics.PanelVoltageMillivolts);
        Assert.Equal(200, metrics.PanelPowerWatts);
        Assert.Equal(250, metrics.LoadCurrentMilliamps);
        Assert.Equal(120, metrics.TodayYieldWattHours);
        Assert.Equal(210, metrics.TodayMaximumPowerWatts);
        Assert.Equal(3, metrics.ChargerState);
        Assert.Equal(0, metrics.ErrorCode);
        Assert.Equal(2, metrics.TrackerState);
        Assert.Equal(1, metrics.LoadState);
    }

    [Fact]
    public async Task WriteAsync_FirstDeviceAtThreshold_WritesSharedDeviceBatch()
    {
        var calls = new ConcurrentQueue<string[]>();
        using var client = new PayloadClient(
            "ve_direct",
            "collector",
            (points, _) =>
            {
                calls.Enqueue(points.Select(ToLineProtocol).ToArray());
                return Task.CompletedTask;
            });

        for (var index = 0; index < 9; index++)
        {
            await client.WriteAsync(new MetricsTransmissionModel("HQ111"), CancellationToken.None);
        }

        for (var index = 0; index < 4; index++)
        {
            await client.WriteAsync(new MetricsTransmissionModel("HQ222"), CancellationToken.None);
        }

        Assert.Empty(calls);

        await client.WriteAsync(new MetricsTransmissionModel("HQ111"), CancellationToken.None);
        await client.FlushAsync(CancellationToken.None);

        var payload = Assert.Single(calls);
        Assert.Equal(70, payload.Length);
        Assert.Equal(50, payload.Count(line => line.Contains("device=HQ111", StringComparison.Ordinal)));
        Assert.Equal(20, payload.Count(line => line.Contains("device=HQ222", StringComparison.Ordinal)));

        for (var index = 0; index < 6; index++)
        {
            await client.WriteAsync(new MetricsTransmissionModel("HQ222"), CancellationToken.None);
        }

        Assert.Single(calls);

        for (var index = 0; index < 4; index++)
        {
            await client.WriteAsync(new MetricsTransmissionModel("HQ222"), CancellationToken.None);
        }

        await client.FlushAsync(CancellationToken.None);
        Assert.Equal(2, calls.Count);
    }

    [Fact]
    public async Task WriteAsync_ThresholdReachedDuringWrite_QueuesNextBatchWithoutOverlap()
    {
        var calls = new ConcurrentQueue<string[]>();
        var firstWriterEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstWriter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var activeWriters = 0;
        var maximumActiveWriters = 0;
        using var client = new PayloadClient(
            "ve_direct",
            "collector",
            async (points, cancellationToken) =>
            {
                calls.Enqueue(points.Select(ToLineProtocol).ToArray());
                var active = Interlocked.Increment(ref activeWriters);
                InterlockedExtensions.Max(ref maximumActiveWriters, active);
                try
                {
                    if (calls.Count == 1)
                    {
                        firstWriterEntered.SetResult();
                        await releaseFirstWriter.Task.WaitAsync(cancellationToken);
                    }
                }
                finally
                {
                    Interlocked.Decrement(ref activeWriters);
                }
            });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        for (var index = 0; index < 9; index++)
        {
            await client.WriteAsync(new MetricsTransmissionModel("HQ111"), timeout.Token);
        }

        var firstWrite = client.WriteAsync(new MetricsTransmissionModel("HQ111"), timeout.Token);
        await firstWriterEntered.Task.WaitAsync(timeout.Token);

        for (var index = 0; index < 10; index++)
        {
            await client.WriteAsync(new MetricsTransmissionModel("HQ222"), timeout.Token);
        }

        releaseFirstWriter.SetResult();
        await Task.WhenAll(firstWrite, client.FlushAsync(timeout.Token));

        Assert.Equal(2, calls.Count);
        Assert.Equal(1, maximumActiveWriters);
        Assert.All(calls, payload => Assert.Equal(50, payload.Length));
    }

    [Fact]
    public async Task WriteAsync_FailedBatch_IsRetriedWithNextBatch()
    {
        var calls = new ConcurrentQueue<string[]>();
        var attempt = 0;
        using var client = new PayloadClient(
            "ve_direct",
            "collector",
            (points, _) =>
            {
                calls.Enqueue(points.Select(ToLineProtocol).ToArray());
                if (Interlocked.Increment(ref attempt) == 1)
                {
                    throw new InvalidOperationException("InfluxDB unavailable");
                }

                return Task.CompletedTask;
            });

        for (var index = 0; index < 9; index++)
        {
            await client.WriteAsync(new MetricsTransmissionModel("HQ111"), CancellationToken.None);
        }

        await client.WriteAsync(new MetricsTransmissionModel("HQ111"), CancellationToken.None);

        for (var index = 0; index < 10; index++)
        {
            await client.WriteAsync(new MetricsTransmissionModel("HQ222"), CancellationToken.None);
        }

        await client.FlushAsync(CancellationToken.None);
        Assert.Equal(2, calls.Count);
        Assert.Equal(50, calls.ElementAt(0).Length);
        Assert.Equal(100, calls.ElementAt(1).Length);
    }

    [Fact]
    public async Task WriteAsync_FailsAfterNextThreshold_RetriesWithoutWaitingForAnotherEvent()
    {
        var calls = new ConcurrentQueue<int>();
        var firstWriterEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failFirstWriter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempt = 0;
        using var client = new PayloadClient(
            "ve_direct",
            "collector",
            async (points, cancellationToken) =>
            {
                calls.Enqueue(points.Count);
                if (Interlocked.Increment(ref attempt) == 1)
                {
                    firstWriterEntered.SetResult();
                    await failFirstWriter.Task.WaitAsync(cancellationToken);
                    throw new InvalidOperationException("InfluxDB unavailable");
                }
            });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        for (var index = 0; index < 9; index++)
        {
            await client.WriteAsync(new MetricsTransmissionModel("HQ111"), timeout.Token);
        }

        var firstWrite = client.WriteAsync(new MetricsTransmissionModel("HQ111"), timeout.Token);
        await firstWriterEntered.Task.WaitAsync(timeout.Token);

        for (var index = 0; index < 10; index++)
        {
            await client.WriteAsync(new MetricsTransmissionModel("HQ222"), timeout.Token);
        }

        failFirstWriter.SetResult();
        await Task.WhenAll(firstWrite, client.FlushAsync(timeout.Token));

        Assert.Equal([50, 100], calls.ToArray());
    }

    [Fact]
    public async Task FlushAsync_DuringTriggeredWrite_IsSerializedAndIncludesNewPoints()
    {
        var calls = new ConcurrentQueue<int>();
        var firstWriterEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstWriter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var activeWriters = 0;
        var maximumActiveWriters = 0;
        using var client = new PayloadClient(
            "ve_direct",
            "collector",
            async (points, cancellationToken) =>
            {
                calls.Enqueue(points.Count);
                var active = Interlocked.Increment(ref activeWriters);
                InterlockedExtensions.Max(ref maximumActiveWriters, active);
                try
                {
                    if (calls.Count == 1)
                    {
                        firstWriterEntered.SetResult();
                        await releaseFirstWriter.Task.WaitAsync(cancellationToken);
                    }
                }
                finally
                {
                    Interlocked.Decrement(ref activeWriters);
                }
            });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        for (var index = 0; index < 9; index++)
        {
            await client.WriteAsync(new MetricsTransmissionModel("HQ111"), timeout.Token);
        }

        var triggeredWrite = client.WriteAsync(new MetricsTransmissionModel("HQ111"), timeout.Token);
        await firstWriterEntered.Task.WaitAsync(timeout.Token);
        await client.WriteAsync(new MetricsTransmissionModel("HQ222"), timeout.Token);
        var explicitFlush = client.FlushAsync(timeout.Token);

        releaseFirstWriter.SetResult();
        await Task.WhenAll(triggeredWrite, explicitFlush);

        Assert.Equal([50, 5], calls.ToArray());
        Assert.Equal(1, maximumActiveWriters);
    }

    [Fact]
    public async Task FlushAsync_CanceledSharedWriter_RetriesWithFlushToken()
    {
        var calls = new ConcurrentQueue<int>();
        var firstWriterEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempt = 0;
        using var writerCancellation = new CancellationTokenSource();
        using var client = new PayloadClient(
            "ve_direct",
            "collector",
            async (points, cancellationToken) =>
            {
                calls.Enqueue(points.Count);
                if (Interlocked.Increment(ref attempt) == 1)
                {
                    firstWriterEntered.SetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
            },
            writerCancellationToken: writerCancellation.Token);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        for (var index = 0; index < 9; index++)
        {
            await client.WriteAsync(new MetricsTransmissionModel("HQ111"), timeout.Token);
        }

        var canceledWrite = client.WriteAsync(new MetricsTransmissionModel("HQ111"), timeout.Token);
        await firstWriterEntered.Task.WaitAsync(timeout.Token);
        await client.WriteAsync(new MetricsTransmissionModel("HQ222"), timeout.Token);
        writerCancellation.Cancel();

        await client.FlushAsync(timeout.Token);
        await canceledWrite;

        Assert.Equal([50, 55], calls.ToArray());
    }

    [Fact]
    public async Task FlushAsync_BufferLimit_DiscardsOldestCompleteFrames()
    {
        var calls = new ConcurrentQueue<string[]>();
        using var client = new PayloadClient(
            "ve_direct",
            "collector",
            (points, _) =>
            {
                calls.Enqueue(points.Select(ToLineProtocol).ToArray());
                return Task.CompletedTask;
            },
            eventsPerWrite: 100,
            maxBufferedPoints: 10);

        await client.WriteAsync(new MetricsTransmissionModel("HQ111"), CancellationToken.None);
        await client.WriteAsync(new MetricsTransmissionModel("HQ222"), CancellationToken.None);
        await client.WriteAsync(new MetricsTransmissionModel("HQ333"), CancellationToken.None);
        await client.FlushAsync(CancellationToken.None);

        var payload = Assert.Single(calls);
        Assert.Equal(10, payload.Length);
        Assert.DoesNotContain(payload, line => line.Contains("device=HQ111", StringComparison.Ordinal));
        Assert.Equal(5, payload.Count(line => line.Contains("device=HQ222", StringComparison.Ordinal)));
        Assert.Equal(5, payload.Count(line => line.Contains("device=HQ333", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task FlushAsync_FailedWrite_PropagatesErrorAndRetainsPoints()
    {
        var calls = new ConcurrentQueue<int>();
        var attempt = 0;
        using var client = new PayloadClient(
            "ve_direct",
            "collector",
            (points, _) =>
            {
                calls.Enqueue(points.Count);
                if (Interlocked.Increment(ref attempt) == 1)
                {
                    throw new InvalidOperationException("InfluxDB unavailable");
                }

                return Task.CompletedTask;
            });

        await client.WriteAsync(new MetricsTransmissionModel("HQ111"), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.FlushAsync(CancellationToken.None));
        await client.FlushAsync(CancellationToken.None);

        Assert.Equal([5, 5], calls.ToArray());
    }

    [Fact]
    public async Task FlushAsync_LessThanThreshold_WritesRemainingPointsOnce()
    {
        var calls = new ConcurrentQueue<int>();
        using var client = new PayloadClient(
            "ve_direct",
            "collector",
            (points, _) =>
            {
                calls.Enqueue(points.Count);
                return Task.CompletedTask;
            });

        await client.WriteAsync(new MetricsTransmissionModel("HQ111"), CancellationToken.None);
        await client.FlushAsync(CancellationToken.None);
        await client.FlushAsync(CancellationToken.None);

        Assert.Equal([5], calls.ToArray());
    }

    private static string ToLineProtocol(PointData point)
    {
        return point.ToLineProtocol(new PointSettings());
    }
}

internal static class InterlockedExtensions
{
    internal static void Max(ref int target, int value)
    {
        var current = Volatile.Read(ref target);
        while (current < value)
        {
            var previous = Interlocked.CompareExchange(ref target, value, current);
            if (previous == current)
            {
                return;
            }

            current = previous;
        }
    }
}
