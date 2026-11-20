using System.Threading.Channels;
using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using InfluxDB.Client.Writes;

namespace Ve.Direct.InfluxDB.Collector.Metrics;

internal sealed class MetricsClient : IFrameOutput
{
    private const int MaxFramesPerBatch = 100;
    private readonly string metricPrefix;
    private readonly string hostName;
    private readonly Func<List<PointData>, CancellationToken, Task> writePointsToDataBase;
    private readonly InfluxDBClient? influxDBClient;
    private readonly Func<CancellationToken, Task> waitForNextWrite;
    private readonly bool calculateMissingMetrics;
    private readonly CancellationTokenSource writerCancellation;
    private readonly ChannelWriter<List<PointData>> payloadWriter;
    private readonly Task writerTask;
    private int disposeState;

    internal MetricsClient(CollectorConfiguration configuration, CancellationToken writerCancellationToken)
        : this(
            configuration.InfluxMetricPrefix,
            Environment.MachineName,
            CreateDatabaseWriter(configuration),
            TimeSpan.FromSeconds(configuration.InfluxWriteInterval),
            configuration.CalculateMissingMetrics,
            writerCancellationToken)
    {
    }

    internal MetricsClient(
        string metricPrefix,
        string hostName,
        Func<List<PointData>, CancellationToken, Task> writePoints,
        TimeSpan? writeInterval = null,
        bool calculateMissingMetrics = true,
        CancellationToken writerCancellationToken = default,
        Func<CancellationToken, Task>? waitForNextWrite = null)
        : this(
            metricPrefix,
            hostName,
            new DatabaseWriter(null, writePoints),
            writeInterval ?? TimeSpan.FromSeconds(10),
            calculateMissingMetrics,
            writerCancellationToken,
            waitForNextWrite)
    {
    }

    private MetricsClient(
        string metricPrefix,
        string hostName,
        DatabaseWriter databaseWriter,
        TimeSpan writeInterval,
        bool calculateMissingMetrics,
        CancellationToken writerCancellationToken,
        Func<CancellationToken, Task>? waitForNextWrite = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(writeInterval, TimeSpan.Zero);

        this.metricPrefix = metricPrefix;
        this.hostName = hostName;
        this.writePointsToDataBase = databaseWriter.WritePoints;
        this.influxDBClient = databaseWriter.Client;
        this.waitForNextWrite = waitForNextWrite ?? (token => Task.Delay(writeInterval, token));
        this.calculateMissingMetrics = calculateMissingMetrics;
        this.writerCancellation = CancellationTokenSource.CreateLinkedTokenSource(writerCancellationToken);
        // shortcut: backlog lives in RAM; use a disk spool if it must survive long outages or restarts.
        var payloadChannel = Channel.CreateUnbounded<List<PointData>>(new UnboundedChannelOptions { SingleReader = true });
        this.payloadWriter = payloadChannel.Writer;
        this.writerTask = this.ConsumePayloadsAsync(payloadChannel.Reader);
    }

    public void WriteFrameData(string portName, IReadOnlyDictionary<string, string> frame, CancellationToken ct)
    {
        try
        {
            var metrics = MetricsCompositor.ComposeMetrics(frame, this.calculateMissingMetrics);
            ConsoleLogger.Debug($"Received metrics for device {metrics.SerialNumber} on {portName}.");
            ct.ThrowIfCancellationRequested();
            var payload = this.CreatePayload(metrics, DateTime.UtcNow);
            if (payload.Count > 0 && !this.payloadWriter.TryWrite(payload))
            {
                throw new ObjectDisposedException(nameof(MetricsClient));
            }
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
        if (Interlocked.Exchange(ref this.disposeState, 1) != 0)
        {
            return;
        }

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
        var points = new List<PointData>(5);
        this.AddMeasurement(points, "battery", metrics, timestamp,
            ("voltage", metrics.BatteryVoltageMillivolts),
            ("current", metrics.BatteryCurrentMilliamps),
            ("power", metrics.CalculatedBatteryPowerMilliwatts));
        this.AddMeasurement(points, "panel", metrics, timestamp,
            ("voltage", metrics.PanelVoltageMillivolts),
            ("current", metrics.CalculatedPanelCurrentMilliamps),
            ("power", metrics.PanelPowerWatts));
        this.AddMeasurement(points, "load", metrics, timestamp,
            ("current", metrics.LoadCurrentMilliamps),
            ("power", metrics.CalculatedLoadPowerMilliwatts),
            ("Status", metrics.LoadState));
        this.AddMeasurement(points, "today", metrics, timestamp,
            ("yield", metrics.TodayYieldWattHours),
            ("power", metrics.TodayMaximumPowerWatts));
        this.AddMeasurement(points, "VICTRON", metrics, timestamp,
            ("CS_Status", metrics.ChargerState),
            ("ERR_Status", metrics.ErrorCode),
            ("MPPT_Status", metrics.TrackerState));
        return points;
    }

    private static DatabaseWriter CreateDatabaseWriter(CollectorConfiguration configuration)
    {
        var client = new InfluxDBClient(
            new InfluxDBClientOptions.Builder()
                .Url(configuration.InfluxDbUrl)
                .Bucket(configuration.InfluxDbBucket)
                .Org(configuration.InfluxDbOrg)
                .Build());
        var writeApi = client.GetWriteApiAsync();
        return new DatabaseWriter(
            client,
            (points, cancellationToken) => writeApi.WritePointsAsync(points, cancellationToken: cancellationToken));
    }

    private void AddMeasurement(
        List<PointData> points,
        string measurement,
        MetricsTransmissionModel metrics,
        DateTime timestamp,
        params (string Name, long? Value)[] fields)
    {
        var point = PointData.Measurement($"{this.metricPrefix}_{measurement}")
            .Tag("host", this.hostName)
            .Tag("device", metrics.SerialNumber);
        foreach (var (name, value) in fields)
        {
            if (value is not null)
            {
                point = point.Field(name, value.Value);
            }
        }

        if (point.HasFields())
        {
            points.Add(point.Timestamp(timestamp, WritePrecision.Ms));
        }
    }

    private async Task ConsumePayloadsAsync(ChannelReader<List<PointData>> payloadReader)
    {
        var ct = this.writerCancellation.Token;
        List<PointData> batch = [];
        try
        {
            while (true)
            {
                await this.waitForNextWrite(ct).ConfigureAwait(false);
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    if (batch.Count == 0)
                    {
                        for (var frames = 0; frames < MaxFramesPerBatch && payloadReader.TryRead(out var payload); frames++)
                        {
                            batch.AddRange(payload);
                        }
                    }

                    if (batch.Count == 0)
                    {
                        break;
                    }

                    try
                    {
                        await this.writePointsToDataBase(batch, ct).WaitAsync(ct).ConfigureAwait(false);
                        ConsoleLogger.Debug($"InfluxDB write completed: {batch.Count} points sent.");
                        batch = [];
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException || !ct.IsCancellationRequested)
                    {
                        ConsoleLogger.Error(exception);
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    private sealed record DatabaseWriter(
        InfluxDBClient? Client,
        Func<List<PointData>, CancellationToken, Task> WritePoints);
}
