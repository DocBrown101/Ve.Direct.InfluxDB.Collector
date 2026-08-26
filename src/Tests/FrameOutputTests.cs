namespace Tests;

using Ve.Direct.InfluxDB.Collector;
using Xunit;

public sealed class FrameOutputTests
{
    [Fact]
    public async Task ConsoleFrameOutput_WriteFrameAsync_WritesFrameToConfiguredWriter()
    {
        using var writer = new StringWriter();
        using var output = new ConsoleFrameOutput(writer);
        var frame = new Dictionary<string, string>
        {
            ["SER#"] = "HQ111",
            ["PID"] = "0xA060",
            ["V"] = "13000"
        };

        await output.WriteFrameAsync("/dev/ttyUSB0", frame, CancellationToken.None);

        Assert.Equal(
            $"Device: HQ111 (/dev/ttyUSB0){Environment.NewLine}"
            + $"KeyValue: SER# - HQ111{Environment.NewLine}"
            + $"KeyValue: PID - 0xA060 (SmartSolar MPPT 100/20 48V){Environment.NewLine}"
            + $"KeyValue: V - 13000{Environment.NewLine}"
            + $"---{Environment.NewLine}",
            writer.ToString());
    }
}
