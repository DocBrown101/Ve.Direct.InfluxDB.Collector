namespace Tests;

using System.Collections.Concurrent;
using Ve.Direct.InfluxDB.Collector.SerialPorts;
using Xunit;

public sealed class VEDirectPortMonitorTests
{
    [Fact]
    public async Task MonitorPortsAsync_FrameWithoutSerial_IsNotPublished()
    {
        var published = new ConcurrentQueue<(string Port, string Serial, string Voltage)>();
        var validFrameSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var monitor = CreateMonitor(
            () => ["/dev/ttyUSB0"],
            async (port, callback, cancellationToken) =>
            {
                callback(Frame(null, "13000"), cancellationToken);
                callback(Frame("HQ111", "12900"), cancellationToken);
                callback(Frame(null, "12800"), cancellationToken);
                validFrameSent.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            },
            (port, data, _) =>
            {
                published.Enqueue((port, data["SER#"], data["V"]));
            });

        await RunUntilAsync(monitor, token => validFrameSent.Task.WaitAsync(token));

        var frame = Assert.Single(published);
        Assert.Equal(("/dev/ttyUSB0", "HQ111", "12900"), frame);
    }

    [Fact]
    public async Task MonitorPortsAsync_DeviceMovesToNewPort_KeepsSerialIdentity()
    {
        string[] ports = ["/dev/ttyUSB0"];
        var published = new ConcurrentQueue<(string Port, string Serial)>();
        var firstFrame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var twoFrames = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var monitor = CreateMonitor(
            () => Volatile.Read(ref ports),
            async (port, callback, cancellationToken) =>
            {
                callback(Frame("HQ111", "13000"), cancellationToken);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            },
            (port, data, _) =>
            {
                published.Enqueue((port, data["SER#"]));
                if (published.Count == 1)
                {
                    firstFrame.TrySetResult();
                }

                if (published.Count == 2)
                {
                    twoFrames.TrySetResult();
                }
            });

        await RunUntilAsync(monitor, async token =>
        {
            await firstFrame.Task.WaitAsync(token);
            Volatile.Write(ref ports, ["/dev/ttyUSB2"]);
            await twoFrames.Task.WaitAsync(token);
        });

        Assert.Equal(
            [("/dev/ttyUSB0", "HQ111"), ("/dev/ttyUSB2", "HQ111")],
            published.ToArray());
    }

    [Fact]
    public async Task MonitorPortsAsync_ReaderFailure_DoesNotStopOtherPort()
    {
        var published = new ConcurrentQueue<string>();
        var healthyReaderCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var monitor = CreateMonitor(
            () => ["/dev/ttyUSB0", "/dev/ttyUSB1"],
            async (port, callback, cancellationToken) =>
            {
                if (port.EndsWith('0'))
                {
                    throw new IOException("Simulated disconnect");
                }

                for (var index = 0; index < 3; index++)
                {
                    callback(Frame("HQ222", (12500 + index).ToString()), cancellationToken);
                }
                healthyReaderCompleted.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            },
            (_, data, _) =>
            {
                published.Enqueue(data["V"]);
            });

        await RunUntilAsync(monitor, token => healthyReaderCompleted.Task.WaitAsync(token));

        Assert.Equal(["12500", "12501", "12502"], published.ToArray());
    }

    [Fact]
    public async Task MonitorPortsAsync_ReaderFailure_ReconnectsPort()
    {
        var attempts = 0;
        var published = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var monitor = CreateMonitor(
            () => ["/dev/ttyUSB0"],
            async (_, callback, cancellationToken) =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    throw new IOException("Simulated disconnect");
                }

                callback(Frame("HQ111", "13000"), cancellationToken);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            },
            (_, data, _) =>
            {
                published.TrySetResult(data["SER#"]);
            });

        await RunUntilAsync(monitor, token => published.Task.WaitAsync(token));

        Assert.Equal("HQ111", await published.Task);
        Assert.True(attempts >= 2);
    }

    [Fact]
    public async Task MonitorPortsAsync_OutputFailure_DoesNotRestartReaderOrStopLaterFrames()
    {
        var readerStarts = 0;
        var processedFrames = 0;
        var secondFrameProcessed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var monitor = CreateMonitor(
            () => ["/dev/ttyUSB0"],
            async (_, callback, cancellationToken) =>
            {
                Interlocked.Increment(ref readerStarts);
                callback(Frame("HQ111", "13000"), cancellationToken);
                callback(Frame("HQ111", "12900"), cancellationToken);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            },
            (_, _, _) =>
            {
                if (Interlocked.Increment(ref processedFrames) == 1)
                {
                    throw new InvalidOperationException("Simulated database failure");
                }

                secondFrameProcessed.TrySetResult();
            });

        await RunUntilAsync(monitor, token => secondFrameProcessed.Task.WaitAsync(token));

        Assert.Equal(1, readerStarts);
        Assert.Equal(2, processedFrames);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MonitorPortsAsync_DuplicateSerial_TakesOverAfterRemovalOrReadTimeout(bool oldPortStaysConnected)
    {
        string[] ports = ["/dev/ttyUSB9", "/dev/ttyUSB0"];
        var ownerReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var duplicateReported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var takeover = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var published = new ConcurrentQueue<string>();
        var monitor = CreateMonitor(
            () => Volatile.Read(ref ports),
            async (port, callback, cancellationToken) =>
            {
                if (port.EndsWith('9'))
                {
                    if (!ownerReady.Task.IsCompleted)
                    {
                        callback(Frame("HQ111", "13000"), cancellationToken);
                        ownerReady.SetResult();
                        if (oldPortStaysConnected)
                        {
                            await duplicateReported.Task.WaitAsync(cancellationToken);
                            throw new TimeoutException("No data on the old port.");
                        }
                    }
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return;
                }

                await ownerReady.Task.WaitAsync(cancellationToken);
                while (!cancellationToken.IsCancellationRequested)
                {
                    callback(Frame("HQ111", "12500"), cancellationToken);
                    duplicateReported.TrySetResult();
                    if (takeover.Task.IsCompleted)
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    }
                    await Task.Delay(5, cancellationToken);
                }
            },
            (port, _, _) =>
            {
                published.Enqueue(port);
                if (port.EndsWith('0'))
                {
                    takeover.TrySetResult();
                }
            });

        await RunUntilAsync(monitor, async token =>
        {
            await duplicateReported.Task.WaitAsync(token);
            if (!oldPortStaysConnected)
            {
                Assert.Equal(["/dev/ttyUSB9"], published.ToArray());
                Volatile.Write(ref ports, ["/dev/ttyUSB0"]);
            }
            await takeover.Task.WaitAsync(token);
        });

        Assert.Equal(["/dev/ttyUSB9", "/dev/ttyUSB0"], published.ToArray());
    }

    private static VEDirectPortMonitor CreateMonitor(
        Func<string[]> getPortNames,
        ReadPort readPort,
        VEDirectPortMonitor.DeviceFrameHandler processFrame)
    {
        return new VEDirectPortMonitor(
            processFrame,
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(5),
            getPortNames,
            readPort);
    }

    private static async Task RunUntilAsync(
        VEDirectPortMonitor monitor,
        Func<CancellationToken, Task> scenario)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var monitorTask = monitor.MonitorPortsAsync(cancellation.Token);
        try
        {
            await scenario(cancellation.Token);
        }
        finally
        {
            cancellation.Cancel();
            await monitorTask.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        }
    }

    private static IReadOnlyDictionary<string, string> Frame(string? serialNumber, string voltage)
    {
        var frame = new Dictionary<string, string> { ["V"] = voltage };
        if (serialNumber is not null)
        {
            frame["SER#"] = serialNumber;
        }

        return frame;
    }
}
