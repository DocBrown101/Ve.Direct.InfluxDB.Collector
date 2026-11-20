using System.Collections.Concurrent;
using System.IO.Ports;
using Ve.Direct.InfluxDB.Collector.ProtocolReader;

namespace Ve.Direct.InfluxDB.Collector.SerialPorts;

internal sealed class VEDirectPortMonitor(
    VEDirectPortMonitor.DeviceFrameHandler processFrame,
    TimeSpan scanInterval,
    TimeSpan reconnectDelay,
    Func<string[]> getPortNames,
    ReadPort readPort)
{
    private readonly Dictionary<string, ActiveReader> activeReaders = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> serialOwners = new(StringComparer.Ordinal);

    internal VEDirectPortMonitor(
        DeviceFrameHandler processFrame,
        TimeSpan scanInterval)
        : this(
            processFrame,
            scanInterval,
            TimeSpan.FromSeconds(2),
            SerialPort.GetPortNames,
            (portName, callback, ct) => new VEDirectReader(portName).ReadFramesUntilDisconnectedAsync(callback, ct))
    {
    }

    internal async Task MonitorPortsAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await this.ScanPortsAsync().ConfigureAwait(false);
                await Task.Delay(scanInterval, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
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
            discoveredPorts = getPortNames();
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

    private async Task RunPortReaderLoopAsync(string portName, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await this.ReadPortUntilDisconnectedAsync(new ReaderState(portName), ct).ConfigureAwait(false);
                await Task.Delay(reconnectDelay, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    private async Task ReadPortUntilDisconnectedAsync(ReaderState reader, CancellationToken ct)
    {
        try
        {
            await readPort(
                reader.PortName,
                (frame, token) => this.HandleFrame(reader, frame, token),
                ct).ConfigureAwait(false);

            if (!ct.IsCancellationRequested)
            {
                ConsoleLogger.Error($"Serial port {reader.PortName} disconnected.");
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
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

    private void HandleFrame(ReaderState reader, IReadOnlyDictionary<string, string> frame, CancellationToken ct)
    {
        if (!frame.TryGetValue("SER#", out var serialNumber) || string.IsNullOrWhiteSpace(serialNumber))
        {
            if (!reader.HasLoggedMissingSerial)
            {
                ConsoleLogger.Info($"Ignoring VE.Direct frames without a valid SER# on {reader.PortName}.");
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
            this.ProcessFrame(reader.PortName, frame, ct);
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

    private void ProcessFrame(string portName, IReadOnlyDictionary<string, string> frame, CancellationToken ct)
    {
        try
        {
            processFrame(portName, frame, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
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

    internal delegate void DeviceFrameHandler(string portName, IReadOnlyDictionary<string, string> frame, CancellationToken ct);

    private sealed record ActiveReader(CancellationTokenSource StopSignal, Task Completion);
}

internal delegate Task ReadPort(
    string portName,
    Action<IReadOnlyDictionary<string, string>, CancellationToken> processFrame,
    CancellationToken ct);
