namespace Ve.Direct.InfluxDB.Collector;

internal interface IFrameOutput : IDisposable
{
    Task WriteFrameAsync(
        string portName,
        IReadOnlyDictionary<string, string> frame,
        CancellationToken cancellationToken);

    Task CompleteAsync(CancellationToken cancellationToken);
}
