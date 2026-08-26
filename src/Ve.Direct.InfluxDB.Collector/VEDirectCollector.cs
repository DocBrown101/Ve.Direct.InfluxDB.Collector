using Ve.Direct.InfluxDB.Collector.SerialPorts;

namespace Ve.Direct.InfluxDB.Collector;

internal sealed class VEDirectCollector
{
    private static readonly TimeSpan OutputCompletionTimeout = TimeSpan.FromSeconds(10);
    private readonly IFrameOutput frameOutput;
    private readonly Func<CancellationToken, Task> monitorPorts;

    internal VEDirectCollector(IFrameOutput frameOutput, TimeSpan scanInterval)
        : this(
            frameOutput,
            new VEDirectPortMonitor(frameOutput.WriteFrameAsync, scanInterval).MonitorPortsAsync)
    {
    }

    internal VEDirectCollector(
        IFrameOutput frameOutput,
        Func<CancellationToken, Task> monitorPorts)
    {
        this.frameOutput = frameOutput;
        this.monitorPorts = monitorPorts;
    }

    internal async Task RunMainLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await this.monitorPorts(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            using var outputCompletion = new CancellationTokenSource(OutputCompletionTimeout);
            await this.frameOutput.CompleteAsync(outputCompletion.Token).ConfigureAwait(false);
        }
    }
}
