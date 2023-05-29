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

        public VEDirectReaderWithChecksum(string serialPortName)
        {
            this.serialPortName = serialPortName ?? SerialPort.GetPortNames().FirstOrDefault() ?? throw new NotSupportedException("No serial port found to read VE.Direct data!");

            ConsoleLogger.Info($"Using Port: {this.serialPortName}");
        }

        public void ReadSerialPortData(Action<Dictionary<string, string>> callbackFunction, CancellationToken ct)
        {
            ConsoleLogger.Info($"Collect Metrics ...");

            var inputData = new Dictionary<string, string>();
            var inputBytes = new List<byte>();
            var currentKey = string.Empty;
            var currentValue = string.Empty;

            using (var serialPort = new SerialPort(this.serialPortName, 19200))
            {
                serialPort.ReadTimeout = 5000;
                serialPort.Open();

                while (!ct.IsCancellationRequested)
                {
                    var inputByte = (byte)serialPort.ReadByte();
                    if (inputByte == 0)
                    {
                        continue;
                    }

                    var inputByteAsChar = Convert.ToChar(inputByte);
                    inputBytes.Add(inputByte);

                    if (inputByteAsChar == '\n' || inputByteAsChar == '\r')
                    {
                        if (!string.IsNullOrEmpty(currentKey) && !string.IsNullOrEmpty(currentValue))
                        {
                            if (currentKey == "Checksum")
                            {
                                this.ProcessData(callbackFunction, inputData, inputBytes);
                                inputData.Clear();
                                inputBytes.Clear();
                            }
                            else
                            {
                                inputData[currentKey] = currentValue;
                            }

                            currentKey = string.Empty;
                            currentValue = string.Empty;
                        }
                    }
                    else if (inputByteAsChar == '\t')
                    {
                        currentKey = currentValue;
                        currentValue = string.Empty;
                    }
                    else
                    {
                        currentValue += inputByteAsChar;
                    }
                }
            }
        }

        private void ProcessData(Action<Dictionary<string, string>> callbackFunction, Dictionary<string, string> data, List<byte> receivedBytes)
        {
            if (this.IsChecksumValid(receivedBytes))
            {
                if (callbackFunction == null)
                {
                    foreach (var entry in data)
                    {
                        var outputValue = entry.Key.ToLower() == "pid" ? entry.Value.GetVictronDeviceNameByPid() : entry.Value;
                        Console.WriteLine("KeyValue: {0} - {1}", entry.Key, outputValue);
                    }
                    Console.WriteLine("---");
                }
                else
                {
                    callbackFunction(data);
                }
            }
            else
            {
                ConsoleLogger.Info("Warning: Checksum is invalid!");
            }
        }

        private bool IsChecksumValid(List<byte> receivedBytes)
        {
            var checksum = 0;

            for (var i = 0; i < receivedBytes.Count - 1; i++)
            {
                checksum += receivedBytes[i];
            }

            checksum %= 256;
            var calculatedChecksum = (0x100 - checksum) % 0x100;
            var receivedChecksum = receivedBytes[receivedBytes.Count - 1];

            ConsoleLogger.Debug($"Calculated Checksum: {calculatedChecksum:X2}");
            ConsoleLogger.Debug($"Received Checksum: {receivedChecksum:X2}");

            return receivedChecksum == calculatedChecksum;
        }
    }
}
