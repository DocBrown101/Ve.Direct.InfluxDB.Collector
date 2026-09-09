namespace Ve.Direct.InfluxDB.Collector;

internal interface IFrameOutput : IDisposable
{
    void WriteFrameData(string portName, IReadOnlyDictionary<string, string> frame, CancellationToken ct);
}
