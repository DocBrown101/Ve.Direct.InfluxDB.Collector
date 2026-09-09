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
        using var client = CreateClient((_, _) => Task.CompletedTask);

        var firstLine = ToLineProtocol(Assert.Single(client.CreatePayload(first, timestamp)));
        var secondLine = ToLineProtocol(Assert.Single(client.CreatePayload(second, timestamp)));

        Assert.Contains("device=HQ111", firstLine, StringComparison.Ordinal);
        Assert.Contains("voltage=13000i", firstLine, StringComparison.Ordinal);
        Assert.Contains("device=HQ222", secondLine, StringComparison.Ordinal);
        Assert.Contains("voltage=12500i", secondLine, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposeMetrics_MissingSerial_Throws()
    {
        var data = new Dictionary<string, string> { ["V"] = "13000" };

        Assert.Throws<ArgumentException>(() => MetricsCompositor.ComposeMetrics(data, false));
    }

    [Fact]
    public void CreatePayload_MissingFields_DoesNotWriteZeroValues()
    {
        var metrics = MetricsCompositor.ComposeMetrics(
            new Dictionary<string, string> { ["SER#"] = "HQ111", ["V"] = "13000" },
            true);
        using var client = CreateClient((_, _) => Task.CompletedTask);

        var line = ToLineProtocol(Assert.Single(client.CreatePayload(metrics, DateTime.UtcNow)));

        Assert.StartsWith("ve_direct_battery,", line, StringComparison.Ordinal);
        Assert.Contains("voltage=13000i", line, StringComparison.Ordinal);
        Assert.DoesNotContain("current=", line, StringComparison.Ordinal);
        Assert.DoesNotContain("power=", line, StringComparison.Ordinal);
    }

    [Fact]
    public void CreatePayload_ReportedZero_WritesZeroValue()
    {
        var metrics = MetricsCompositor.ComposeMetrics(
            new Dictionary<string, string>
            {
                ["SER#"] = "HQ111",
                ["V"] = "0",
                ["I"] = "-500",
                ["IL"] = "0",
                ["LOAD"] = "ON"
            },
            true);
        using var client = CreateClient((_, _) => Task.CompletedTask);

        var lines = client.CreatePayload(metrics, DateTime.UtcNow).Select(ToLineProtocol).ToArray();

        Assert.Contains(lines, line => line.Contains("voltage=0i", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("current=0i", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("Status=1i", StringComparison.Ordinal));
    }

    [Fact]
    public void ComposeMetrics_KnownFields_MapsValuesAndUnits()
    {
        var metrics = MetricsCompositor.ComposeMetrics(Frame("HQ111", "13000"), false);

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
    public async Task WriteFrameData_InvalidThenValidFrame_WritesValidFrameOnNextInterval()
    {
        using var ticks = new SemaphoreSlim(0);
        var writeCompleted = new TaskCompletionSource<List<PointData>>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = CreateClient((points, _) =>
        {
            writeCompleted.TrySetResult(points);
            return Task.CompletedTask;
        }, ticks);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        client.WriteFrameData(
            "test",
            new Dictionary<string, string> { ["SER#"] = "HQ111", ["V"] = "invalid" },
            timeout.Token);
        client.WriteFrameData(
            "test",
            new Dictionary<string, string> { ["SER#"] = "HQ111", ["V"] = "13000" },
            timeout.Token);

        ticks.Release();
        var payload = await writeCompleted.Task.WaitAsync(timeout.Token);
        Assert.Contains(payload, point => ToLineProtocol(point).Contains("voltage=13000i", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TestConstructor_DefaultsToProductionMetricCalculation()
    {
        using var ticks = new SemaphoreSlim(0);
        var writeCompleted = new TaskCompletionSource<List<PointData>>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = CreateClient((points, _) =>
        {
            writeCompleted.TrySetResult(points);
            return Task.CompletedTask;
        }, ticks);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        client.WriteFrameData(
            "test",
            new Dictionary<string, string> { ["SER#"] = "HQ111", ["V"] = "13000", ["I"] = "500" },
            timeout.Token);

        ticks.Release();
        var battery = Assert.Single(await writeCompleted.Task.WaitAsync(timeout.Token));
        Assert.Contains("power=6500i", ToLineProtocol(battery), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Writer_ProcessesAtMostOneHundredFramesPerInterval()
    {
        using var ticks = new SemaphoreSlim(0);
        var calls = new ConcurrentQueue<List<PointData>>();
        var firstWriteCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondWriteCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = CreateClient(
            (points, _) =>
            {
                calls.Enqueue(points);
                if (calls.Count == 1)
                {
                    firstWriteCompleted.TrySetResult();
                }
                else if (calls.Count == 2)
                {
                    secondWriteCompleted.TrySetResult();
                }

                return Task.CompletedTask;
            },
            ticks,
            maxQueuedFrames: 101);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        for (var index = 0; index < 101; index++)
        {
            WriteFrameData(client, $"HQ{index}", timeout.Token);
        }

        ticks.Release();
        await firstWriteCompleted.Task.WaitAsync(timeout.Token);
        ticks.Release();
        await secondWriteCompleted.Task.WaitAsync(timeout.Token);

        Assert.Equal(500, calls.ElementAt(0).Count);
        Assert.Equal(5, calls.ElementAt(1).Count);
    }

    [Fact]
    public async Task Writer_FailedBatch_IsRetriedUnchanged()
    {
        using var ticks = new SemaphoreSlim(0);
        var calls = new ConcurrentQueue<string[]>();
        var firstAttemptCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var retryCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nextBatchCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = CreateClient((points, _) =>
        {
            calls.Enqueue(points.Select(ToLineProtocol).ToArray());
            if (calls.Count == 1)
            {
                firstAttemptCompleted.TrySetResult();
                throw new InvalidOperationException("InfluxDB unavailable");
            }

            if (calls.Count == 2)
            {
                retryCompleted.TrySetResult();
            }
            else
            {
                nextBatchCompleted.TrySetResult();
            }

            return Task.CompletedTask;
        }, ticks);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        WriteFrameData(client, "HQ111", timeout.Token);
        ticks.Release();
        await firstAttemptCompleted.Task.WaitAsync(timeout.Token);
        WriteFrameData(client, "HQ222", timeout.Token);
        ticks.Release();
        await retryCompleted.Task.WaitAsync(timeout.Token);

        Assert.Equal(2, calls.Count);
        Assert.Equal(calls.ElementAt(0), calls.ElementAt(1));
        Assert.DoesNotContain(calls.ElementAt(1), line => line.Contains("device=HQ222", StringComparison.Ordinal));

        ticks.Release();
        await nextBatchCompleted.Task.WaitAsync(timeout.Token);
        Assert.All(calls.ElementAt(2), line => Assert.Contains("device=HQ222", line, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Writer_FullQueue_DropsOldestFrame()
    {
        using var ticks = new SemaphoreSlim(0);
        var writeCompleted = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = CreateClient(
            (points, _) =>
            {
                writeCompleted.TrySetResult(points.Select(ToLineProtocol).ToArray());
                return Task.CompletedTask;
            },
            ticks,
            maxQueuedFrames: 2);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        WriteFrameData(client, "HQ111", timeout.Token);
        WriteFrameData(client, "HQ222", timeout.Token);
        WriteFrameData(client, "HQ333", timeout.Token);

        ticks.Release();
        var payload = await writeCompleted.Task.WaitAsync(timeout.Token);
        Assert.DoesNotContain(payload, line => line.Contains("device=HQ111", StringComparison.Ordinal));
        Assert.Equal(5, payload.Count(line => line.Contains("device=HQ222", StringComparison.Ordinal)));
        Assert.Equal(5, payload.Count(line => line.Contains("device=HQ333", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task WriteFrameData_BlockedWriter_DoesNotBlockProducers()
    {
        using var ticks = new SemaphoreSlim(0);
        var calls = new ConcurrentQueue<string[]>();
        var firstWriterEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstWriter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondWriteCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = CreateClient(
            async (points, cancellationToken) =>
            {
                calls.Enqueue(points.Select(ToLineProtocol).ToArray());
                if (calls.Count == 1)
                {
                    firstWriterEntered.TrySetResult();
                    await releaseFirstWriter.Task.WaitAsync(cancellationToken);
                }
                else
                {
                    secondWriteCompleted.TrySetResult();
                }
            },
            ticks,
            maxQueuedFrames: 1);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        WriteFrameData(client, "HQ111", timeout.Token);
        ticks.Release();
        await firstWriterEntered.Task.WaitAsync(timeout.Token);

        for (var index = 0; index < 20; index++)
        {
            WriteFrameData(client, "HQ222", timeout.Token, (12000 + index).ToString());
        }

        releaseFirstWriter.SetResult();
        ticks.Release();
        await secondWriteCompleted.Task.WaitAsync(timeout.Token);

        Assert.Equal(2, calls.Count);
        Assert.Contains(calls.ElementAt(1), line => line.Contains("voltage=12019i", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Dispose_BlockedWriter_DoesNotWaitForUnderlyingWrite()
    {
        using var ticks = new SemaphoreSlim(0);
        var writerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWriter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = CreateClient(async (_, _) =>
        {
            writerEntered.TrySetResult();
            await releaseWriter.Task;
        }, ticks);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        WriteFrameData(client, "HQ111", timeout.Token);
        ticks.Release();
        await writerEntered.Task.WaitAsync(timeout.Token);

        var disposeTask = Task.Run(client.Dispose, TestContext.Current.CancellationToken);
        try
        {
            await disposeTask.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        }
        finally
        {
            releaseWriter.TrySetResult();
        }
    }

    private static MetricsClient CreateClient(
        Func<List<PointData>, CancellationToken, Task> writePoints,
        SemaphoreSlim? ticks = null,
        int maxQueuedFrames = 2_000)
    {
        return new MetricsClient(
            "ve_direct",
            "collector",
            writePoints,
            TimeSpan.FromSeconds(10),
            maxQueuedFrames,
            waitForNextWrite: ticks is null ? null : ticks.WaitAsync);
    }

    private static string ToLineProtocol(PointData point)
    {
        return point.ToLineProtocol(new PointSettings());
    }

    private static void WriteFrameData(
        MetricsClient client,
        string serialNumber,
        CancellationToken cancellationToken,
        string voltage = "13000")
    {
        client.WriteFrameData("test", Frame(serialNumber, voltage), cancellationToken);
    }

    private static Dictionary<string, string> Frame(string serialNumber, string voltage)
    {
        return new Dictionary<string, string>
        {
            ["SER#"] = serialNumber,
            ["V"] = voltage,
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
    }
}
