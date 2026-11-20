using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Text;

namespace Ve.Direct.InfluxDB.Collector.ProtocolReader;

internal sealed class VEDirectReader(string serialPortName)
{
    private const int MaxFrameBytes = 4096;
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);
    private readonly Dictionary<string, string> currentFrame = [];
    private readonly StringBuilder field = new();
    private ReadState state = ReadState.SeekHeader;
    private int frameLength;
    private byte checksumSum;
    private string currentKey = string.Empty;

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
                this.state = ReadState.SeekHeader;
            }

            return false;
        }

        if (this.frameLength == MaxFrameBytes
            || (this.state == ReadState.InKey && character is '\r' or '\n')
            || (this.state == ReadState.WaitHeader && character != '\n'))
        {
            this.ResetFrame();
        }

        if (this.state == ReadState.SeekHeader && character != '\r')
        {
            return false;
        }

        this.frameLength++;
        this.checksumSum += inputByte;
        switch (this.state)
        {
            case ReadState.SeekHeader:
                this.state = ReadState.WaitHeader;
                break;
            case ReadState.WaitHeader:
                this.state = ReadState.InKey;
                break;
            case ReadState.InKey:
                if (character == '\t')
                {
                    this.currentKey = this.field.ToString();
                    this.field.Clear();
                    this.state = this.currentKey == "Checksum" ? ReadState.InChecksum : ReadState.InValue;
                }
                else
                {
                    this.field.Append(character);
                }
                break;
            case ReadState.InValue:
                if (character == '\r')
                {
                    this.currentFrame[this.currentKey] = this.field.ToString();
                    this.field.Clear();
                    this.state = ReadState.WaitHeader;
                }
                else
                {
                    this.field.Append(character);
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
        Action<IReadOnlyDictionary<string, string>, CancellationToken> processFrame,
        CancellationToken cancellationToken)
    {
        using var serialPort = new SerialPort(serialPortName, 19200);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var cancellationRegistration = cancellationToken.Register(serialPort.Dispose);
            serialPort.Open();
            ConsoleLogger.Info($"Opened serial port {serialPortName}.");
            var buffer = new byte[256];

            while (!cancellationToken.IsCancellationRequested)
            {
                var bytesRead = await serialPort.BaseStream
                    .ReadAsync(buffer.AsMemory(), cancellationToken).AsTask()
                    .WaitAsync(ReadTimeout, cancellationToken)
                    .ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    throw new IOException($"Serial port {serialPortName} was disconnected.");
                }

                for (var index = 0; index < bytesRead; index++)
                {
                    if (this.ProcessInputByte(buffer[index]))
                    {
                        processFrame(this.LastFrame, cancellationToken);
                    }
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
        this.field.Clear();
        this.frameLength = 0;
        this.checksumSum = 0;
        this.currentKey = string.Empty;
        this.state = ReadState.SeekHeader;
    }

    private enum ReadState
    {
        Hex,
        SeekHeader,
        WaitHeader,
        InKey,
        InValue,
        InChecksum
    }
}
