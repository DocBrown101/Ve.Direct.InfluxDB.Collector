using System.Globalization;

namespace Ve.Direct.InfluxDB.Collector.Metrics;

internal sealed class MetricsCompositor : IDisposable
{
    private readonly bool calculateMissingMetrics;
    private readonly PayloadClient payloadClient;

    internal MetricsCompositor(CollectorConfiguration configuration)
    {
        this.calculateMissingMetrics = configuration.CalculateMissingMetrics;
        this.payloadClient = new PayloadClient(configuration);
    }

    internal async Task SendMetricsAsync(
        string portName,
        IReadOnlyDictionary<string, string> frame,
        CancellationToken cancellationToken)
    {
        try
        {
            var metrics = ComposeMetrics(frame, this.calculateMissingMetrics);
            ConsoleLogger.Debug($"Received metrics for device {metrics.SerialNumber} on {portName}.");
            await this.payloadClient.WriteAsync(metrics, cancellationToken).ConfigureAwait(false);
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
        this.payloadClient.Dispose();
    }

    internal static MetricsTransmissionModel ComposeMetrics(
        IReadOnlyDictionary<string, string> frame,
        bool calculateMissingMetrics)
    {
        if (!frame.TryGetValue("SER#", out var serialNumber) || string.IsNullOrWhiteSpace(serialNumber))
        {
            throw new ArgumentException("A VE.Direct frame must contain SER# before metrics can be composed.", nameof(frame));
        }

        var metrics = new MetricsTransmissionModel(serialNumber.Trim());
        foreach (var (key, value) in frame)
        {
            switch (key)
            {
                case "V": // Battery voltage (mV)
                    metrics.BatteryVoltageMillivolts = ParseLong(value);
                    break;
                case "I": // Battery current (mA)
                    metrics.BatteryCurrentMilliamps = ParseLong(value);
                    break;
                case "VPV": // Panel voltage (mV)
                    metrics.PanelVoltageMillivolts = ParseLong(value);
                    break;
                case "PPV": // Panel power (W)
                    metrics.PanelPowerWatts = ParseLong(value);
                    break;
                case "IL": // Load current (mA)
                    metrics.LoadCurrentMilliamps = ParseLong(value);
                    break;
                case "H20": // Yield today (0.01 kWh)
                    metrics.TodayYieldWattHours = ParseLong(value) * 10;
                    break;
                case "H21": // Maximum power today (W)
                    metrics.TodayMaximumPowerWatts = ParseLong(value);
                    break;
                case "CS": // State of operation
                    metrics.ChargerState = ParseInt(value);
                    break;
                case "ERR": // Error state
                    metrics.ErrorCode = ParseInt(value);
                    break;
                case "MPPT": // Tracker operation mode
                    metrics.TrackerState = ParseInt(value);
                    break;
                case "LOAD": // Load output State (ON/OFF)
                    metrics.LoadState = value == "ON" ? 1 : 0;
                    break;
            }
        }

        if (calculateMissingMetrics)
        {
            metrics.CalculateMissingMetrics();
        }

        return metrics;
    }

    private static long ParseLong(string value)
    {
        return long.Parse(value, CultureInfo.InvariantCulture);
    }

    private static int ParseInt(string value)
    {
        return int.Parse(value, CultureInfo.InvariantCulture);
    }
}
