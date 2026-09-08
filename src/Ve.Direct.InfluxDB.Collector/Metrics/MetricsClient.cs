using System.Threading.Channels;
using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using InfluxDB.Client.Writes;

namespace Ve.Direct.InfluxDB.Collector.Metrics;

internal sealed class MetricsClient : IFrameOutput
{
    private const int PointsPerPayload = 5;

    private readonly string metricPrefix;
    private readonly string hostName;
    private readonly Func<List<PointData>, CancellationToken, Task> writePointsToDataBase;
    private readonly InfluxDBClient? influxDBClient;
    private readonly int eventsPerWrite;
    private readonly int maxBufferedPoints;
    private readonly bool calculateMissingMetrics;
    private readonly CancellationTokenSource writerCancellation;
    private readonly ChannelWriter<DevicePayload> payloadWriter;
    private readonly Task writerTask;
    private bool isDisposed;

    internal MetricsClient(CollectorConfiguration configuration, CancellationToken writerCancellationToken)
    {
        this.metricPrefix = configuration.InfluxMetricPrefix;
        this.hostName = Environment.MachineName;
        this.eventsPerWrite = configuration.InfluxEventsPerWrite;
        this.maxBufferedPoints = configuration.InfluxMaxBufferedPoints;
        this.calculateMissingMetrics = configuration.CalculateMissingMetrics;
        this.writerCancellation = CancellationTokenSource.CreateLinkedTokenSource(writerCancellationToken);
        this.influxDBClient = new InfluxDBClient(
            new InfluxDBClientOptions.Builder()
                .Url(configuration.InfluxDbUrl)
                .Bucket(configuration.InfluxDbBucket)
                .Org(configuration.InfluxDbOrg)
                .Build());
        var writeApi = this.influxDBClient.GetWriteApiAsync();
        this.writePointsToDataBase = (points, cancellationToken) => writeApi.WritePointsAsync(
            points,
            configuration.InfluxDbBucket,
            configuration.InfluxDbOrg,
            cancellationToken);
        var payloadChannel = CreatePayloadChannel(this.maxBufferedPoints);
        this.payloadWriter = payloadChannel.Writer;
        this.writerTask = this.ConsumePayloadsAsync(payloadChannel.Reader);
    }

    internal MetricsClient(
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
        this.writePointsToDataBase = writePoints;
        this.eventsPerWrite = eventsPerWrite;
        this.maxBufferedPoints = maxBufferedPoints;
        this.writerCancellation = CancellationTokenSource.CreateLinkedTokenSource(writerCancellationToken);
        var payloadChannel = CreatePayloadChannel(this.maxBufferedPoints);
        this.payloadWriter = payloadChannel.Writer;
        this.writerTask = this.ConsumePayloadsAsync(payloadChannel.Reader);
    }

    public async Task WriteFrameAsync(string portName, IReadOnlyDictionary<string, string> frame, CancellationToken ct)
    {
        try
        {
            var metrics = MetricsCompositor.ComposeMetrics(frame, this.calculateMissingMetrics);
            ConsoleLogger.Debug($"Received metrics for device {metrics.SerialNumber} on {portName}.");
            ct.ThrowIfCancellationRequested();
            var payload = new DevicePayload(
                metrics.SerialNumber,
                this.CreatePayload(metrics, DateTime.UtcNow));
            await this.payloadWriter.WriteAsync(payload, ct).ConfigureAwait(false);
        }
        catch (ArgumentException exception)
        {
            ConsoleLogger.Warning($"Metrics from {portName} were ignored: {exception.Message}");
        }
        catch (FormatException exception)
        {
            ConsoleLogger.Warning($"Metrics from {portName} contain an invalid number: {exception.Message}");
        }
        catch (OverflowException exception)
        {
            ConsoleLogger.Warning($"Metrics from {portName} contain a number outside the supported range: {exception.Message}");
        }
    }

    public void Dispose()
    {
        if (this.isDisposed)
        {
            return;
        }

        this.isDisposed = true;
        this.payloadWriter.TryComplete();
        this.writerCancellation.Cancel();
        try
        {
            this.writerTask.GetAwaiter().GetResult();
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

    private async Task ConsumePayloadsAsync(ChannelReader<DevicePayload> payloadReader)
    {
        var deviceEventCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var pendingPayloads = new Queue<DevicePayload>();
        var bufferedPointCount = 0;

        await foreach (var payload in payloadReader.ReadAllAsync().ConfigureAwait(false))
        {
            pendingPayloads.Enqueue(payload);
            bufferedPointCount += payload.Points.Count;
            this.TrimBuffer(pendingPayloads, ref bufferedPointCount);

            deviceEventCounts.TryGetValue(payload.SerialNumber, out var eventCount);
            deviceEventCounts[payload.SerialNumber] = ++eventCount;
            if (eventCount < this.eventsPerWrite)
            {
                continue;
            }

            deviceEventCounts.Clear();
            try
            {
                var points = pendingPayloads.SelectMany(payload => payload.Points).ToList();
                await this.writePointsToDataBase(points, this.writerCancellation.Token)
                    .WaitAsync(this.writerCancellation.Token)
                    .ConfigureAwait(false);
                pendingPayloads.Clear();
                bufferedPointCount = 0;
                ConsoleLogger.Debug($"InfluxDB write completed: {points.Count} points sent.");
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

    private void TrimBuffer(Queue<DevicePayload> pendingPayloads, ref int bufferedPointCount)
    {
        var droppedPoints = 0;
        while (bufferedPointCount > this.maxBufferedPoints
               && pendingPayloads.TryDequeue(out var droppedPayload))
        {
            droppedPoints += droppedPayload.Points.Count;
            bufferedPointCount -= droppedPayload.Points.Count;
        }

        if (droppedPoints > 0)
        {
            ConsoleLogger.Warning($"InfluxDB buffer limit reached: {droppedPoints} oldest points were discarded.");
        }
    }

    private sealed record DevicePayload(string SerialNumber, List<PointData> Points);
}
