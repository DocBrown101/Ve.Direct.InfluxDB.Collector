using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Threading;

namespace Ve.Direct.InfluxDB.Collector
{
    public class VEDirectReader
    {
        private readonly SerialPort serialPort;
        private readonly char header1;
        private readonly char header2;
        private readonly char hexmarker;
        private readonly char delimiter;
        private string key;
        private string value;
        private ReadState state;
        private readonly Dictionary<string, string> dict;
        private byte bytes_sum;

        private enum ReadState
        {
            HEX,
            WAIT_HEADER,
            IN_KEY,
            IN_VALUE,
            IN_CHECKSUM
        }

        public VEDirectReader()
        {
            ConsoleLogger.Info($"Collect Metrics ...");

            var serialPortName = SerialPort.GetPortNames().FirstOrDefault() ?? throw new NotSupportedException("No serial port found to read VEDirect.");

            ConsoleLogger.Info($"Using Port: {serialPortName}");

            this.serialPort = new SerialPort(serialPortName, 19200, Parity.None, 8, StopBits.One);
            this.serialPort.Open();

            this.header1 = '\r';
            this.header2 = '\n';
            this.hexmarker = ':';
            this.delimiter = '\t';
            this.key = "";
            this.value = "";
            this.bytes_sum = 0;
            this.state = ReadState.WAIT_HEADER;
            this.dict = new Dictionary<string, string>();
        }

        public Dictionary<string, string> ProcessInputByte(byte inputByte)
        {
            var inputByteAsChar = Convert.ToChar(inputByte);
            if (inputByteAsChar == this.hexmarker && this.state != ReadState.IN_CHECKSUM)
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
                    if (inputByteAsChar == this.header1) this.state = ReadState.WAIT_HEADER;
                    else if (inputByteAsChar == this.header2) this.state = ReadState.IN_KEY;
                    break;

                case ReadState.IN_KEY:
                    if (inputByteAsChar == this.delimiter)
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
                    if (inputByteAsChar == this.header1)
                    {
                        this.state = ReadState.WAIT_HEADER;
                        if (this.dict.ContainsKey(this.key))
                            this.dict[this.key] = this.value;
                        else
                            this.dict.Add(this.key, this.value);
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
                        return this.dict;
                    }
                    Console.WriteLine("Warning: bytes_sum = {0}", this.bytes_sum);
                    this.bytes_sum = 0;
                    break;

                case ReadState.HEX:
                    this.bytes_sum = 0;
                    if (inputByteAsChar == this.header2) this.state = ReadState.WAIT_HEADER;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(string.Format("Unknown readstate {0}", this.state));
            }
            return null;
        }

        public void WritePortDataToConsole(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var inputByte = (byte)this.serialPort.ReadByte();
                if (inputByte != 0)
                {
                    var packet = this.ProcessInputByte(inputByte);
                    if (packet != null)
                    {
                        foreach (var kvp in packet)
                        {
                            if (kvp.Key.ToLower() == "pid")
                            {
                                Console.WriteLine(kvp.Key.GetVictronDeviceNameByPid());
                            }
                            else
                            {
                                Console.WriteLine("KeyValue: {0} - {1}", kvp.Key, kvp.Value);
                            }

                        }
                    }
                }
            }
        }

        public void ReadPortData(Action<Dictionary<string, string>> callbackFunction, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var inputByte = (byte)this.serialPort.ReadByte();
                if (inputByte != 0)
                {
                    var packet = this.ProcessInputByte(inputByte);
                    if (packet != null)
                    {
                        callbackFunction(packet);
                    }
                }
            }
        }
    }
}
