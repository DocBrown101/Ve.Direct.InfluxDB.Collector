using System.IO.Ports;
using Ve.Direct.InfluxDB.Collector.ProtocolReader;

namespace Ve.Direct.InfluxDB.Collector.SerialPorts;

internal sealed class PortSource(Func<string[]> getPortNames, ReadPort readPort)
{
    internal static PortSource System { get; } = new(
        SerialPort.GetPortNames,
        (portName, processFrame, ct) =>
            new VEDirectReader(portName).ReadFramesUntilDisconnectedAsync(processFrame, ct));

    internal Func<string[]> GetPortNames { get; } = getPortNames;

    internal ReadPort ReadAsync { get; } = readPort;
}

internal delegate Task ReadPort(
        string portName,
        Action<IReadOnlyDictionary<string, string>, CancellationToken> processFrame,
        CancellationToken ct);
