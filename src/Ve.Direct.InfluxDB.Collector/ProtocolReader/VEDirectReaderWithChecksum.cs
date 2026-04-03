namespace Ve.Direct.InfluxDB.Collector.ProtocolReader
{
    using System;
    using System.Collections.Generic;
    using System.IO.Ports;
    using System.Linq;
    using System.Threading;

    public class VEDirectReaderWithChecksum : IReader
    {
        private readonly string serialPortName;
        private readonly Dictionary<string, string> serialData = new();
        private readonly List<byte> inputBytes = new();
        private string currentKey = "";
        private string currentValue = "";
        private bool isInHexMode;

        public VEDirectReaderWithChecksum(string serialPortName)
        {
            this.serialPortName = serialPortName ?? SerialPort.GetPortNames().FirstOrDefault() ?? throw new NotSupportedException("No serial port found to read VE.Direct data!");
            ConsoleLogger.Info($"Using Port: {this.serialPortName}");
        }

        public bool ProcessInputByte(byte inputByte)
        {
            var inputByteAsChar = Convert.ToChar(inputByte);

            // ':' outside of the Checksum field value triggers hex mode
            if (inputByteAsChar == ':' && this.currentKey != "Checksum")
            {
                this.isInHexMode = true;
                this.inputBytes.Clear();
                return false;
            }

            if (this.isInHexMode)
            {
                if (inputByteAsChar == '\n')
                    this.isInHexMode = false;
                return false;
            }

            this.inputBytes.Add(inputByte);

            if (inputByteAsChar == '\r' || inputByteAsChar == '\n')
            {
                if (!string.IsNullOrEmpty(this.currentKey) && !string.IsNullOrEmpty(this.currentValue))
                {
                    this.serialData[this.currentKey] = this.currentValue;
                }
                this.currentKey = "";
                this.currentValue = "";
            }
            else if (inputByteAsChar == '\t')
            {
                this.currentKey = this.currentValue;
                this.currentValue = "";
            }
            else if (this.currentKey == "Checksum")
            {
                var valid = IsChecksumValid(this.inputBytes);
                this.inputBytes.Clear();
                this.currentKey = "";
                this.currentValue = "";
                return valid;
            }
            else
            {
                this.currentValue += inputByteAsChar;
            }

            return false;
        }

        public void ReadSerialPortData(Action<Dictionary<string, string>> callbackFunction, CancellationToken ct)
        {
            ConsoleLogger.Info($"Collect Metrics ...");

            using var serialPort = new SerialPort(this.serialPortName, 19200);
            serialPort.ReadTimeout = 5000;
            serialPort.Open();

            while (!ct.IsCancellationRequested)
            {
                var inputByte = (byte)serialPort.ReadByte();
                if (inputByte == 0)
                    continue;

                if (this.ProcessInputByte(inputByte))
                {
                    callbackFunction(this.serialData);
                }
            }
        }

        private static bool IsChecksumValid(List<byte> receivedBytes)
        {
            byte sum = 0;
            foreach (var b in receivedBytes)
                sum += b;
            return sum == 0;
        }
    }
}
