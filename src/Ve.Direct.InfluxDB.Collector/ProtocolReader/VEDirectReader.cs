using System.Collections.ObjectModel;
using System.IO.Ports;

namespace Ve.Direct.InfluxDB.Collector.ProtocolReader;

internal sealed class VEDirectReader(string serialPortName)
{
    private readonly Dictionary<string, string> currentFrame = [];
    private ReadState state = ReadState.WaitHeader;
    private byte checksumSum;
    private string currentKey = string.Empty;
    private string currentValue = string.Empty;

    internal IReadOnlyDictionary<string, string> LastFrame { get; private set; }
        = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());

    internal bool ProcessInputByte(byte inputByte)
    {
        var character = Convert.ToChar(inputByte);

        if (character == ':' && this.state != ReadState.InChecksum)
        {
            this.ResetFrame();
            this.state = ReadState.Hex;
            return false;
        }

        if (this.state == ReadState.Hex)
        {
            if (character == '\n')
            {
                this.state = ReadState.WaitHeader;
            }

            return false;
        }

        this.checksumSum += inputByte;
        switch (this.state)
        {
            case ReadState.WaitHeader:
                if (character == '\n')
                {
                    this.state = ReadState.InKey;
                }
                break;
            case ReadState.InKey:
                if (character == '\t')
                {
                    this.state = this.currentKey == "Checksum" ? ReadState.InChecksum : ReadState.InValue;
                }
                else
                {
                    this.currentKey += character;
                }
                break;
            case ReadState.InValue:
                if (character == '\r')
                {
                    this.currentFrame[this.currentKey] = this.currentValue;
                    this.currentKey = string.Empty;
                    this.currentValue = string.Empty;
                    this.state = ReadState.WaitHeader;
                }
                else
                {
                    this.currentValue += character;
                }
                break;
            case ReadState.InChecksum:
                var isValid = this.checksumSum == 0;
                if (isValid)
                {
                    this.LastFrame = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(this.currentFrame));
                }
                else
                {
                    ConsoleLogger.Info($"Ignoring VE.Direct frame with invalid checksum ({this.checksumSum}).");
                }

                this.ResetFrame();
                return isValid;
            default:
                throw new ArgumentOutOfRangeException(nameof(this.state), this.state, "Unknown reader state.");
        }

        return false;
    }

    internal async Task ReadFramesUntilDisconnectedAsync(
        Func<IReadOnlyDictionary<string, string>, CancellationToken, Task> processFrame,
        CancellationToken cancellationToken)
    {
        using var serialPort = new SerialPort(serialPortName, 19200);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var cancellationRegistration = cancellationToken.Register(serialPort.Dispose);
            serialPort.Open();
            ConsoleLogger.Info($"Opened serial port {serialPortName}.");
            var buffer = new byte[1];

            while (!cancellationToken.IsCancellationRequested)
            {
                var bytesRead = await serialPort.BaseStream
                    .ReadAsync(buffer.AsMemory(0, 1), cancellationToken)
                    .ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    throw new IOException($"Serial port {serialPortName} was disconnected.");
                }

                if (this.ProcessInputByte(buffer[0]))
                {
                    await processFrame(this.LastFrame, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            // Disposing SerialPort to unblock a read produces platform-specific exception types.
        }
    }

    private void ResetFrame()
    {
        this.currentFrame.Clear();
        this.checksumSum = 0;
        this.currentKey = string.Empty;
        this.currentValue = string.Empty;
        this.state = ReadState.WaitHeader;
    }

    private enum ReadState
    {
        Hex,
        WaitHeader,
        InKey,
        InValue,
        InChecksum
    }
}
