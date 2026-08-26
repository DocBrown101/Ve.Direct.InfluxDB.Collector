using System.Collections.Concurrent;
using System.IO.Ports;
using Ve.Direct.InfluxDB.Collector.ProtocolReader;

namespace Ve.Direct.InfluxDB.Collector.SerialPorts;

internal sealed class VEDirectPortMonitor
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(2);

    private readonly MonitoringIntervals intervals;
    private readonly DeviceFrameHandler processFrame;
    private readonly PortSource serialPorts;
    private readonly Dictionary<string, ActiveReader> activeReaders = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> serialOwners = new(StringComparer.Ordinal);

    internal VEDirectPortMonitor(
        DeviceFrameHandler processFrame,
        TimeSpan scanInterval)
        : this(processFrame, new MonitoringIntervals(scanInterval, ReconnectDelay), PortSource.System)
    {
    }

    internal VEDirectPortMonitor(
        DeviceFrameHandler processFrame,
        MonitoringIntervals intervals,
        PortSource serialPorts)
    {
        this.processFrame = processFrame;
        this.intervals = intervals;
        this.serialPorts = serialPorts;
    }

    internal async Task MonitorPortsAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await this.ScanPortsAsync().ConfigureAwait(false);
                await Task.Delay(this.intervals.Scan, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            await this.StopReadersAsync(this.activeReaders.Keys.ToArray()).ConfigureAwait(false);
        }
    }

    private async Task ScanPortsAsync()
    {
        string[] discoveredPorts;
        try
        {
            discoveredPorts = this.serialPorts.GetPortNames();
        }
        catch (Exception exception)
        {
            ConsoleLogger.Error($"Serial port discovery failed: {exception.Message}");
            return;
        }

        var desiredPorts = discoveredPorts
            .Where(port => !string.IsNullOrWhiteSpace(port))
            .ToHashSet(StringComparer.Ordinal);
        var removedPorts = this.activeReaders.Keys.Where(port => !desiredPorts.Contains(port)).ToArray();
        await this.StopReadersAsync(removedPorts).ConfigureAwait(false);

        foreach (var port in desiredPorts.Where(port => !this.activeReaders.ContainsKey(port)).Order(StringComparer.Ordinal))
        {
            var cancellation = new CancellationTokenSource();
            var task = Task.Run(() => this.RunPortReaderLoopAsync(port, cancellation.Token), CancellationToken.None);
            this.activeReaders.Add(port, new ActiveReader(cancellation, task));
            ConsoleLogger.Info($"Monitoring serial port {port}.");
        }
    }

    private async Task RunPortReaderLoopAsync(string portName, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await this.ReadPortUntilDisconnectedAsync(new ReaderState(portName), cancellationToken)
                    .ConfigureAwait(false);
                await Task.Delay(this.intervals.Reconnect, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task ReadPortUntilDisconnectedAsync(
        ReaderState reader,
        CancellationToken cancellationToken)
    {
        try
        {
            await this.serialPorts.ReadAsync(
                reader.PortName,
                (frame, token) => this.HandleFrameAsync(reader, frame, token),
                cancellationToken).ConfigureAwait(false);

            if (!cancellationToken.IsCancellationRequested)
            {
                ConsoleLogger.Error($"Serial port {reader.PortName} disconnected.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            ConsoleLogger.Error($"Serial port {reader.PortName} failed: {exception.Message}");
        }
        finally
        {
            this.ReleaseIdentity(reader.PortName, reader.SerialNumber);
        }
    }

    private async Task HandleFrameAsync(
        ReaderState reader,
        IReadOnlyDictionary<string, string> frame,
        CancellationToken cancellationToken)
    {
        if (!frame.TryGetValue("SER#", out var serialNumber) || string.IsNullOrWhiteSpace(serialNumber))
        {
            if (!reader.HasLoggedMissingSerial)
            {
                ConsoleLogger.Info($"Ignoring VE.Direct data from {reader.PortName} until a valid SER# is received.");
                reader.HasLoggedMissingSerial = true;
            }

            return;
        }

        reader.HasLoggedMissingSerial = false;
        serialNumber = serialNumber.Trim();
        if (!StringComparer.Ordinal.Equals(reader.SerialNumber, serialNumber))
        {
            this.ReleaseIdentity(reader.PortName, reader.SerialNumber);
            reader.SerialNumber = serialNumber;
            reader.LastDuplicateSerialNumber = null;
        }

        if (this.serialOwners.TryAdd(serialNumber, reader.PortName))
        {
            ConsoleLogger.Info($"Detected VE.Direct device {serialNumber} on {reader.PortName}.");
        }

        if (this.serialOwners.TryGetValue(serialNumber, out var owner)
            && StringComparer.Ordinal.Equals(owner, reader.PortName))
        {
            reader.LastDuplicateSerialNumber = null;
            await this.ProcessFrameAsync(reader.PortName, frame, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (StringComparer.Ordinal.Equals(reader.LastDuplicateSerialNumber, serialNumber))
        {
            return;
        }

        reader.LastDuplicateSerialNumber = serialNumber;
        ConsoleLogger.Warning(
            $"Duplicate VE.Direct device {serialNumber} detected on {owner} and {reader.PortName}; {reader.PortName} is suppressed.");
    }

    private async Task ProcessFrameAsync(
        string portName,
        IReadOnlyDictionary<string, string> frame,
        CancellationToken cancellationToken)
    {
        try
        {
            await this.processFrame(portName, frame, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            ConsoleLogger.Error($"Processing a frame from {portName} failed: {exception.Message}");
        }
    }

    private async Task StopReadersAsync(string[] ports)
    {
        var stoppedReaders = ports.Select(port => (Port: port, Reader: this.activeReaders[port])).ToArray();
        foreach (var (port, reader) in stoppedReaders)
        {
            this.activeReaders.Remove(port);
            reader.StopSignal.Cancel();
        }

        try
        {
            await Task.WhenAll(stoppedReaders.Select(entry => entry.Reader.Completion)).ConfigureAwait(false);
        }
        finally
        {
            foreach (var (port, reader) in stoppedReaders)
            {
                reader.StopSignal.Dispose();
                ConsoleLogger.Info($"Stopped monitoring serial port {port}.");
            }
        }
    }

    private void ReleaseIdentity(string portName, string? serialNumber)
    {
        if (serialNumber is not null
            && this.serialOwners.TryRemove(KeyValuePair.Create(serialNumber, portName)))
        {
            ConsoleLogger.Info($"Device {serialNumber} disconnected from {portName}.");
        }
    }

    internal delegate Task DeviceFrameHandler(
        string portName,
        IReadOnlyDictionary<string, string> frame,
        CancellationToken cancellationToken);

    internal delegate Task ReadPort(
        string portName,
        Func<IReadOnlyDictionary<string, string>, CancellationToken, Task> processFrame,
        CancellationToken cancellationToken);

    internal sealed class PortSource(Func<string[]> getPortNames, ReadPort readPort)
    {
        internal static PortSource System { get; } = new(
            SerialPort.GetPortNames,
            (portName, processFrame, cancellationToken) =>
                new VEDirectReader(portName).ReadFramesUntilDisconnectedAsync(processFrame, cancellationToken));

        internal string[] GetPortNames()
        {
            return getPortNames();
        }

        internal Task ReadAsync(
            string portName,
            Func<IReadOnlyDictionary<string, string>, CancellationToken, Task> processFrame,
            CancellationToken cancellationToken)
        {
            return readPort(portName, processFrame, cancellationToken);
        }
    }

    internal sealed record MonitoringIntervals(TimeSpan Scan, TimeSpan Reconnect);

    private sealed record ActiveReader(CancellationTokenSource StopSignal, Task Completion);

    private sealed class ReaderState(string portName)
    {
        internal string PortName { get; } = portName;

        internal string? SerialNumber { get; set; }

        internal string? LastDuplicateSerialNumber { get; set; }

        internal bool HasLoggedMissingSerial { get; set; }
    }
}
