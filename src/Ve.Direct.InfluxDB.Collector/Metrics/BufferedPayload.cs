using InfluxDB.Client.Writes;

namespace Ve.Direct.InfluxDB.Collector.Metrics;

internal sealed record BufferedPayload
{
    internal BufferedPayload(string serialNumber, List<PointData> points)
    {
        this.SerialNumber = serialNumber;
        this.Points = points;
    }

    internal BufferedPayload(TaskCompletionSource flushCompletion, CancellationToken cancellationToken)
    {
        this.FlushCompletion = flushCompletion;
        this.CancellationToken = cancellationToken;
    }

    internal string? SerialNumber { get; }

    internal List<PointData>? Points { get; }

    internal TaskCompletionSource? FlushCompletion { get; }

    internal CancellationToken CancellationToken { get; }
}
