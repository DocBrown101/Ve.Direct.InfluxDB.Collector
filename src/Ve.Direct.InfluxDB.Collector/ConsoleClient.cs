using System.Text;
using Ve.Direct.InfluxDB.Collector.ProtocolReader;

namespace Ve.Direct.InfluxDB.Collector;

internal sealed class ConsoleClient(TextWriter output) : IFrameOutput
{
    internal ConsoleClient()
        : this(Console.Out)
    {
    }

    public void WriteFrameData(string portName, IReadOnlyDictionary<string, string> frame, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
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
    }

    public void Dispose()
    {
    }
}
