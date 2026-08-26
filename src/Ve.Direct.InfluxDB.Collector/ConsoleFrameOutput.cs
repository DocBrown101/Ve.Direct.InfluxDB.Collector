using System.Text;
using Ve.Direct.InfluxDB.Collector.ProtocolReader;

namespace Ve.Direct.InfluxDB.Collector;

internal sealed class ConsoleFrameOutput(TextWriter output) : IFrameOutput
{
    internal ConsoleFrameOutput()
        : this(Console.Out)
    {
    }

    public Task WriteFrameAsync(
        string portName,
        IReadOnlyDictionary<string, string> frame,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var text = new StringBuilder().AppendLine($"Device: {frame["SER#"]} ({portName})");
        foreach (var (key, value) in frame)
        {
            var outputValue = key.Equals("PID", StringComparison.OrdinalIgnoreCase)
                ? value.GetVictronDeviceNameByPid()
                : value;
            text.AppendLine($"KeyValue: {key} - {outputValue}");
        }

        text.AppendLine("---");
        output.Write(text);
        return Task.CompletedTask;
    }

    public Task CompleteAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public void Dispose()
    {
    }
}
