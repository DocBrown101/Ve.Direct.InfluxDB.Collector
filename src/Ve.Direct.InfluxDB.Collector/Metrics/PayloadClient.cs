using System.Threading.Channels;
using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using InfluxDB.Client.Writes;

namespace Ve.Direct.InfluxDB.Collector.Metrics;

internal sealed class PayloadClient : IDisposable
{
    private const int PointsPerPayload = 5;

    private readonly string metricPrefix;
    private readonly string hostName;
    private readonly Func<List<PointData>, CancellationToken, Task> writePoints;
    private readonly InfluxDBClient? influxDBClient;
    private readonly int eventsPerWrite;
    private readonly int maxBufferedPoints;
    private readonly CancellationTokenSource writerCancellation;
    private readonly Channel<DevicePayload> payloadChannel;
    private readonly Task workerTask;
    private bool isDisposed;

    internal PayloadClient(CollectorConfiguration configuration, CancellationToken writerCancellationToken)
    {
        this.metricPrefix = configuration.InfluxMetricPrefix;
        this.hostName = Environment.MachineName;
        this.eventsPerWrite = configuration.InfluxEventsPerWrite;
        this.maxBufferedPoints = configuration.InfluxMaxBufferedPoints;
        this.writerCancellation = CancellationTokenSource.CreateLinkedTokenSource(writerCancellationToken);
        this.influxDBClient = new InfluxDBClient(
            new InfluxDBClientOptions.Builder()
                .Url(configuration.InfluxDbUrl)
                .Bucket(configuration.InfluxDbBucket)
                .Org(configuration.InfluxDbOrg)
                .Build());
        var writeApi = this.influxDBClient.GetWriteApiAsync();
        this.writePoints = (points, cancellationToken) => writeApi.WritePointsAsync(
            points,
            configuration.InfluxDbBucket,
            configuration.InfluxDbOrg,
            cancellationToken);
        this.payloadChannel = CreatePayloadChannel(this.maxBufferedPoints);
        this.workerTask = this.ProcessPayloadsAsync();
    }

    internal PayloadClient(
        string metricPrefix,
        string hostName,
        Func<List<PointData>, CancellationToken, Task> writePoints,
        int eventsPerWrite = 10,
        int maxBufferedPoints = 10_000,
        CancellationToken writerCancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(eventsPerWrite, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBufferedPoints, 5);

        this.metricPrefix = metricPrefix;
        this.hostName = hostName;
        this.writePoints = writePoints;
        this.eventsPerWrite = eventsPerWrite;
        this.maxBufferedPoints = maxBufferedPoints;
        this.writerCancellation = CancellationTokenSource.CreateLinkedTokenSource(writerCancellationToken);
        this.payloadChannel = CreatePayloadChannel(this.maxBufferedPoints);
        this.workerTask = this.ProcessPayloadsAsync();
    }

    internal Task WriteAsync(MetricsTransmissionModel metrics, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var devicePayload = new DevicePayload(
            metrics.SerialNumber,
            this.CreatePayload(metrics, DateTime.UtcNow));
        return this.payloadChannel.Writer.WriteAsync(devicePayload, cancellationToken).AsTask();
    }

    public void Dispose()
    {
        if (this.isDisposed)
        {
            return;
        }

        this.isDisposed = true;
        this.payloadChannel.Writer.TryComplete();
        this.writerCancellation.Cancel();
        try
        {
            this.workerTask.GetAwaiter().GetResult();
        }
        finally
        {
            this.writerCancellation.Dispose();
            this.influxDBClient?.Dispose();
        }
    }

    internal List<PointData> CreatePayload(MetricsTransmissionModel metrics, DateTime timestamp)
    {
        return
        [
            this.AddTags(PointData.Measurement($"{this.metricPrefix}_battery"), metrics)
                .Field("voltage", metrics.BatteryVoltageMillivolts)
                .Field("current", metrics.BatteryCurrentMilliamps)
                .Field("power", metrics.CalculatedBatteryPowerMilliwatts)
                .Timestamp(timestamp, WritePrecision.Ms),
            this.AddTags(PointData.Measurement($"{this.metricPrefix}_panel"), metrics)
                .Field("voltage", metrics.PanelVoltageMillivolts)
                .Field("current", metrics.CalculatedPanelCurrentMilliamps)
                .Field("power", metrics.PanelPowerWatts)
                .Timestamp(timestamp, WritePrecision.Ms),
            this.AddTags(PointData.Measurement($"{this.metricPrefix}_load"), metrics)
                .Field("current", metrics.LoadCurrentMilliamps)
                .Field("power", metrics.CalculatedLoadPowerMilliwatts)
                .Field("Status", metrics.LoadState)
                .Timestamp(timestamp, WritePrecision.Ms),
            this.AddTags(PointData.Measurement($"{this.metricPrefix}_today"), metrics)
                .Field("yield", metrics.TodayYieldWattHours)
                .Field("power", metrics.TodayMaximumPowerWatts)
                .Timestamp(timestamp, WritePrecision.Ms),
            this.AddTags(PointData.Measurement($"{this.metricPrefix}_VICTRON"), metrics)
                .Field("CS_Status", metrics.ChargerState)
                .Field("ERR_Status", metrics.ErrorCode)
                .Field("MPPT_Status", metrics.TrackerState)
                .Timestamp(timestamp, WritePrecision.Ms)
        ];
    }

    private PointData AddTags(PointData point, MetricsTransmissionModel metrics)
    {
        return point
            .Tag("host", this.hostName)
            .Tag("device", metrics.SerialNumber);
    }

    private static Channel<DevicePayload> CreatePayloadChannel(int maxBufferedPoints)
    {
        return Channel.CreateBounded<DevicePayload>(new BoundedChannelOptions(
            Math.Max(1, maxBufferedPoints / PointsPerPayload))
        {
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    private async Task ProcessPayloadsAsync()
    {
        var deviceEventCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var pendingPayloads = new Queue<DevicePayload>();
        var bufferedPointCount = 0;

        await foreach (var payload in this.payloadChannel.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            pendingPayloads.Enqueue(payload);
            bufferedPointCount += payload.Points.Count;
            var droppedPoints = this.TrimBuffer(pendingPayloads, ref bufferedPointCount);
            LogDroppedPoints(droppedPoints);

            deviceEventCounts.TryGetValue(payload.SerialNumber, out var eventCount);
            deviceEventCounts[payload.SerialNumber] = ++eventCount;
            if (eventCount < this.eventsPerWrite)
            {
                continue;
            }

            deviceEventCounts.Clear();
            try
            {
                await this.WritePendingPayloadsAsync(pendingPayloads, this.writerCancellation.Token)
                    .ConfigureAwait(false);
                bufferedPointCount = 0;
            }
            catch (OperationCanceledException) when (this.writerCancellation.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                ConsoleLogger.Error(exception);
            }
        }
    }

    private async Task WritePendingPayloadsAsync(
        Queue<DevicePayload> pendingPayloads,
        CancellationToken cancellationToken)
    {
        if (pendingPayloads.Count == 0)
        {
            return;
        }

        var points = pendingPayloads.SelectMany(payload => payload.Points).ToList();
        await this.writePoints(points, cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);
        pendingPayloads.Clear();
        ConsoleLogger.Debug($"InfluxDB write completed: {points.Count} points sent.");
    }

    private int TrimBuffer(Queue<DevicePayload> pendingPayloads, ref int bufferedPointCount)
    {
        var droppedPoints = 0;
        while (bufferedPointCount > this.maxBufferedPoints
               && pendingPayloads.TryDequeue(out var droppedPayload))
        {
            droppedPoints += droppedPayload.Points.Count;
            bufferedPointCount -= droppedPayload.Points.Count;
        }

        return droppedPoints;
    }

    private static void LogDroppedPoints(int droppedPoints)
    {
        if (droppedPoints > 0)
        {
            ConsoleLogger.Warning($"InfluxDB buffer limit reached: {droppedPoints} oldest points were discarded.");
        }
    }

    private sealed record DevicePayload(string SerialNumber, List<PointData> Points);
}
