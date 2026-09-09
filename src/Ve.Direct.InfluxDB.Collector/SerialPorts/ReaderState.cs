namespace Ve.Direct.InfluxDB.Collector.SerialPorts;

internal sealed class ReaderState(string portName)
{
    internal string PortName { get; } = portName;

    internal string? SerialNumber { get; set; }

    internal string? LastDuplicateSerialNumber { get; set; }

    internal bool HasLoggedMissingSerial { get; set; }
}
