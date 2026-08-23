using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using InfluxDB.Client.Writes;

namespace Ve.Direct.InfluxDB.Collector.Metrics;

internal sealed class PayloadClient : IDisposable
{
    private readonly string metricPrefix;
    private readonly string hostName;
    private readonly Func<List<PointData>, CancellationToken, Task> writePoints;
    private readonly InfluxDBClient? influxDBClient;

    internal PayloadClient(CollectorConfiguration configuration)
    {
        this.metricPrefix = configuration.InfluxMetricPrefix;
        this.hostName = Environment.MachineName;
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
    }

    internal PayloadClient(
        string metricPrefix,
        string hostName,
        Func<List<PointData>, CancellationToken, Task> writePoints)
    {
        this.metricPrefix = metricPrefix;
        this.hostName = hostName;
        this.writePoints = writePoints;
    }

    internal async Task WriteAsync(MetricsTransmissionModel metrics, CancellationToken cancellationToken)
    {
        var points = this.CreatePayload(metrics, DateTime.UtcNow);
        await this.writePoints(points, cancellationToken).ConfigureAwait(false);
        ConsoleLogger.Debug($"InfluxDB write completed: {points.Count} points sent for {metrics.SerialNumber}.");
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

    public void Dispose()
    {
        this.influxDBClient?.Dispose();
    }

    private PointData AddTags(PointData point, MetricsTransmissionModel metrics)
    {
        return point
            .Tag("host", this.hostName)
            .Tag("device", metrics.SerialNumber);
    }
}
