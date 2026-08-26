namespace Tests;

using Ve.Direct.InfluxDB.Collector;
using Xunit;

public sealed class FrameOutputTests
{
    [Fact]
    public async Task CollectorMainLoop_AfterShutdown_CompletesOutputWithIndependentToken()
    {
        using var shutdown = new CancellationTokenSource();
        shutdown.Cancel();
        using var output = new RecordingFrameOutput();
        var collector = new VEDirectCollector(output, TimeSpan.FromSeconds(1));

        await collector.RunMainLoopAsync(shutdown.Token);

        Assert.True(output.CompleteCalled);
        Assert.False(output.CompletionTokenWasCanceled);
    }

    [Fact]
    public async Task CollectorMainLoop_PortMonitorFails_StillCompletesOutput()
    {
        using var output = new RecordingFrameOutput();
        var collector = new VEDirectCollector(
            output,
            _ => throw new IOException("Port monitoring failed."));

        await Assert.ThrowsAsync<IOException>(() => collector.RunMainLoopAsync(CancellationToken.None));

        Assert.True(output.CompleteCalled);
    }

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
        using var canceledCompletion = new CancellationTokenSource();
        canceledCompletion.Cancel();
        await output.CompleteAsync(canceledCompletion.Token);

        Assert.Equal(
            $"Device: HQ111 (/dev/ttyUSB0){Environment.NewLine}"
            + $"KeyValue: SER# - HQ111{Environment.NewLine}"
            + $"KeyValue: PID - 0xA060 (SmartSolar MPPT 100/20 48V){Environment.NewLine}"
            + $"KeyValue: V - 13000{Environment.NewLine}"
            + $"---{Environment.NewLine}",
            writer.ToString());
    }

    private sealed class RecordingFrameOutput : IFrameOutput
    {
        internal bool CompleteCalled { get; private set; }

        internal bool CompletionTokenWasCanceled { get; private set; }

        public void Dispose()
        {
        }

        public Task WriteFrameAsync(
            string portName,
            IReadOnlyDictionary<string, string> frame,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("No frame should be written after shutdown.");
        }

        public Task CompleteAsync(CancellationToken cancellationToken)
        {
            this.CompleteCalled = true;
            this.CompletionTokenWasCanceled = cancellationToken.IsCancellationRequested;
            return Task.CompletedTask;
        }
    }
}
