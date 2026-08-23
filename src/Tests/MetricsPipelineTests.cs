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
    public async Task WriteAsync_ConcurrentDevices_WritesIndependentPayloadsInParallel()
    {
        var calls = new ConcurrentQueue<string[]>();
        var writersEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWriters = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var activeWriters = 0;
        using var client = new PayloadClient(
            "ve_direct",
            "collector",
            async (points, cancellationToken) =>
            {
                calls.Enqueue(points.Select(ToLineProtocol).ToArray());
                if (Interlocked.Increment(ref activeWriters) == 3)
                {
                    writersEntered.TrySetResult();
                }

                try
                {
                    await releaseWriters.Task.WaitAsync(cancellationToken);
                }
                finally
                {
                    Interlocked.Decrement(ref activeWriters);
                }
            });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var writes = new[] { "HQ111", "HQ222", "HQ333" }
            .Select(serial => client.WriteAsync(new MetricsTransmissionModel(serial), timeout.Token))
            .ToArray();
        await writersEntered.Task.WaitAsync(timeout.Token);
        releaseWriters.SetResult();
        await Task.WhenAll(writes);

        Assert.Equal(3, calls.Count);
        foreach (var serial in new[] { "HQ111", "HQ222", "HQ333" })
        {
            var payload = Assert.Single(calls, lines => lines.All(
                line => line.Contains($"device={serial}", StringComparison.Ordinal)));
            Assert.Equal(5, payload.Length);
        }
    }

    private static string ToLineProtocol(PointData point)
    {
        return point.ToLineProtocol(new PointSettings());
    }
}
