using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Threading;

namespace Ve.Direct.InfluxDB.Collector.ProtocolReader
{
    public class VEDirectReader : IReader
    {
        private readonly Dictionary<string, string> serialData;
        private readonly string serialPortName;
        private const char HEADER1 = '\r';
        private const char HEADER2 = '\n';
        private const char HEXMARKER = ':';
        private const char DELIMITER = '\t';

        private ReadState state = ReadState.WAIT_HEADER;
        private byte bytes_sum = 0;
        private string key = string.Empty;
        private string value = string.Empty;

        private enum ReadState
        {
            HEX,
            WAIT_HEADER,
            IN_KEY,
            IN_VALUE,
            IN_CHECKSUM
        }

        public VEDirectReader(string serialPortName)
        {
            this.serialData = new Dictionary<string, string>();
            this.serialPortName = serialPortName ?? SerialPort.GetPortNames().FirstOrDefault() ?? throw new NotSupportedException("No serial port found to read VE.Direct data!");

            ConsoleLogger.Info($"Using Port: {this.serialPortName}");
            ConsoleLogger.Info($"Collect Metrics ...");
        }

        public bool ProcessInputByte(byte inputByte)
        {
            var inputByteAsChar = Convert.ToChar(inputByte);

            if (inputByteAsChar == HEXMARKER && this.state != ReadState.IN_CHECKSUM)
            {
                this.state = ReadState.HEX;
            }

            if (this.state != ReadState.HEX)
            {
                this.bytes_sum += inputByte;
            }

            switch (this.state)
            {
                case ReadState.WAIT_HEADER:
                    if (inputByteAsChar == HEADER1) this.state = ReadState.WAIT_HEADER;
                    else if (inputByteAsChar == HEADER2) this.state = ReadState.IN_KEY;
                    break;

                case ReadState.IN_KEY:
                    if (inputByteAsChar == DELIMITER)
                    {
                        this.state = this.key == "Checksum"
                            ? ReadState.IN_CHECKSUM
                            : ReadState.IN_VALUE;
                    }
                    else
                    {
                        this.key += inputByteAsChar;
                    }
                    break;

                case ReadState.IN_VALUE:
                    if (inputByteAsChar == HEADER1)
                    {
                        this.state = ReadState.WAIT_HEADER;
                        if (this.serialData.ContainsKey(this.key))
                            this.serialData[this.key] = this.value;
                        else
                            this.serialData.Add(this.key, this.value);
                        this.key = "";
                        this.value = "";
                    }
                    else
                    {
                        this.value += inputByteAsChar;
                    }
                    break;

                case ReadState.IN_CHECKSUM:
                    this.key = "";
                    this.value = "";
                    this.state = ReadState.WAIT_HEADER;
                    if (this.bytes_sum == 0)
                    {
                        this.bytes_sum = 0;
                        return true;
                    }
                    ConsoleLogger.Info($"Warning: bytes_sum = {this.bytes_sum}");
                    this.bytes_sum = 0;
                    break;

                case ReadState.HEX:
                    this.bytes_sum = 0;
                    if (inputByteAsChar == HEADER2) this.state = ReadState.WAIT_HEADER;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(string.Format("Unknown readstate {0}", this.state));
            }
            return false;
        }

        public void ReadSerialPortData(Action<Dictionary<string, string>> callbackFunction, CancellationToken ct)
        {
            using var serialPort = new SerialPort(this.serialPortName, 19200);
            serialPort.ReadTimeout = 5000;
            serialPort.Open();

            while (!ct.IsCancellationRequested)
            {
                var inputByte = (byte)serialPort.ReadByte();
                if (inputByte == 0)
                {
                    continue;
                }

                var allBytesReceived = this.ProcessInputByte(inputByte);
                if (allBytesReceived)
                {
                    callbackFunction(this.serialData);
                }
            }
        }
    }
}
