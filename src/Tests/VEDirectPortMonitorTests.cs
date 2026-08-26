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
                await callback(Frame(null, "13000"), cancellationToken);
                await callback(Frame("HQ111", "12900"), cancellationToken);
                validFrameSent.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            },
            (port, data, _) =>
            {
                published.Enqueue((port, data["SER#"], data["V"]));
                return Task.CompletedTask;
            });

        await RunUntilAsync(monitor, validFrameSent.Task);

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
                await callback(Frame("HQ111", "13000"), cancellationToken);
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
                return Task.CompletedTask;
            });

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var monitorTask = monitor.MonitorPortsAsync(cancellation.Token);
        try
        {
            await firstFrame.Task.WaitAsync(cancellation.Token);
            Volatile.Write(ref ports, ["/dev/ttyUSB2"]);
            await twoFrames.Task.WaitAsync(cancellation.Token);
        }
        finally
        {
            cancellation.Cancel();
            await monitorTask.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        }

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
                    await callback(Frame("HQ222", (12500 + index).ToString()), cancellationToken);
                }
                healthyReaderCompleted.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            },
            (_, data, _) =>
            {
                published.Enqueue(data["V"]);
                return Task.CompletedTask;
            });

        await RunUntilAsync(monitor, healthyReaderCompleted.Task);

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

                await callback(Frame("HQ111", "13000"), cancellationToken);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            },
            (_, data, _) =>
            {
                published.TrySetResult(data["SER#"]);
                return Task.CompletedTask;
            });

        await RunUntilAsync(monitor, published.Task);

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
                await callback(Frame("HQ111", "13000"), cancellationToken);
                await callback(Frame("HQ111", "12900"), cancellationToken);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            },
            (_, _, _) =>
            {
                if (Interlocked.Increment(ref processedFrames) == 1)
                {
                    throw new InvalidOperationException("Simulated database failure");
                }

                secondFrameProcessed.TrySetResult();
                return Task.CompletedTask;
            });

        await RunUntilAsync(monitor, secondFrameProcessed.Task);

        Assert.Equal(1, readerStarts);
        Assert.Equal(2, processedFrames);
    }

    [Fact]
    public async Task MonitorPortsAsync_DuplicateSerial_FirstOwnerWinsAndReplacementTakesOverAfterDisconnect()
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
                    await callback(Frame("HQ111", "13000"), cancellationToken);
                    ownerReady.SetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return;
                }

                await ownerReady.Task.WaitAsync(cancellationToken);
                while (!cancellationToken.IsCancellationRequested)
                {
                    await callback(Frame("HQ111", "12500"), cancellationToken);
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
                return Task.CompletedTask;
            });

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var monitorTask = monitor.MonitorPortsAsync(cancellation.Token);
        try
        {
            await duplicateReported.Task.WaitAsync(cancellation.Token);
            Assert.Equal(["/dev/ttyUSB9"], published.ToArray());
            Volatile.Write(ref ports, ["/dev/ttyUSB0"]);
            await takeover.Task.WaitAsync(cancellation.Token);
        }
        finally
        {
            cancellation.Cancel();
            await monitorTask.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        }

        Assert.Equal(["/dev/ttyUSB9", "/dev/ttyUSB0"], published.ToArray());
    }

    private static VEDirectPortMonitor CreateMonitor(
        Func<string[]> getPortNames,
        VEDirectPortMonitor.ReadPort readPort,
        VEDirectPortMonitor.DeviceFrameHandler processFrame)
    {
        var serialPorts = new VEDirectPortMonitor.PortSource(getPortNames, readPort);
        var intervals = new VEDirectPortMonitor.MonitoringIntervals(
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(5));

        return new VEDirectPortMonitor(
            processFrame,
            intervals,
            serialPorts);
    }

    private static async Task RunUntilAsync(VEDirectPortMonitor monitor, Task completion)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var monitorTask = monitor.MonitorPortsAsync(cancellation.Token);
        try
        {
            await completion.WaitAsync(cancellation.Token);
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
