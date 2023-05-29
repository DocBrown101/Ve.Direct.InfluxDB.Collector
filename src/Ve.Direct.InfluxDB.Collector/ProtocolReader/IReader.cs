namespace Ve.Direct.InfluxDB.Collector.ProtocolReader
{
    using System;
    using System.Collections.Generic;
    using System.Threading;

    public interface IReader
    {
        void ReadSerialPortData(Action<Dictionary<string, string>> callbackFunction, CancellationToken ct);
    }
}