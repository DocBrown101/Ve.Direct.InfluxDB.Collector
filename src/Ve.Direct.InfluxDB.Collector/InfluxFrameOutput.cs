using Ve.Direct.InfluxDB.Collector.Metrics;

namespace Ve.Direct.InfluxDB.Collector;

internal sealed class InfluxFrameOutput : IFrameOutput
{
    private readonly bool calculateMissingMetrics;
    private readonly PayloadClient payloadClient;

    internal InfluxFrameOutput(CollectorConfiguration configuration, CancellationToken writerCancellationToken)
    {
        this.calculateMissingMetrics = configuration.CalculateMissingMetrics;
        this.payloadClient = new PayloadClient(configuration, writerCancellationToken);
    }

    public async Task WriteFrameAsync(
        string portName,
        IReadOnlyDictionary<string, string> frame,
        CancellationToken cancellationToken)
    {
        try
        {
            var metrics = MetricsCompositor.ComposeMetrics(frame, this.calculateMissingMetrics);
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
}
