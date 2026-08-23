namespace Tests;

using System.Text;
using Ve.Direct.InfluxDB.Collector.ProtocolReader;
using Xunit;

public sealed class VEDirectReaderTests
{
    [Fact]
    public void ProcessInputByte_CompleteFrame_ParsesSerialAndMetrics()
    {
        var reader = new VEDirectReader("TESTPORT");

        var complete = Feed(reader, BuildMessage(
            ("PID", "0xA060"),
            ("SER#", "HQ123456"),
            ("V", "13310"),
            ("I", "500"),
            ("CS", "3")));

        Assert.True(complete);
        Assert.Equal("0xA060", reader.LastFrame["PID"]);
        Assert.Equal("HQ123456", reader.LastFrame["SER#"]);
        Assert.Equal("13310", reader.LastFrame["V"]);
        Assert.Equal("500", reader.LastFrame["I"]);
        Assert.Equal("3", reader.LastFrame["CS"]);
    }

    [Fact]
    public void ProcessInputByte_PartialFrame_DoesNotComplete()
    {
        var reader = new VEDirectReader("TESTPORT");
        var partialFrame = Encoding.ASCII.GetBytes("\r\nSER#\tHQ123456\r");

        Assert.All(partialFrame, value => Assert.False(reader.ProcessInputByte(value)));
        Assert.Empty(reader.LastFrame);
    }

    [Fact]
    public void ProcessInputByte_InvalidFrame_KeepsPreviousValidSnapshot()
    {
        var reader = new VEDirectReader("TESTPORT");
        Feed(reader, BuildMessage(("SER#", "HQ123456"), ("V", "13000")));
        var invalidFrame = BuildMessage(("SER#", "HQ999999"), ("V", "9000"));
        invalidFrame[^1] ^= 0x01;

        Assert.False(Feed(reader, invalidFrame));
        Assert.Equal("HQ123456", reader.LastFrame["SER#"]);
        Assert.Equal("13000", reader.LastFrame["V"]);
    }

    [Fact]
    public void ProcessInputByte_NextFrame_DoesNotInheritMissingFields()
    {
        var reader = new VEDirectReader("TESTPORT");
        Feed(reader, BuildMessage(("SER#", "HQ123456"), ("V", "13000")));
        var firstFrame = reader.LastFrame;

        Feed(reader, BuildMessage(("V", "12900")));

        Assert.False(reader.LastFrame.ContainsKey("SER#"));
        Assert.Equal("12900", reader.LastFrame["V"]);
        Assert.Equal("HQ123456", firstFrame["SER#"]);
        Assert.Equal("13000", firstFrame["V"]);
    }

    [Fact]
    public void ProcessInputByte_HexFrameBeforeTextFrame_ParsesTextFrame()
    {
        var reader = new VEDirectReader("TESTPORT");
        foreach (var value in Encoding.ASCII.GetBytes(":A12\n"))
        {
            reader.ProcessInputByte(value);
        }

        Assert.True(Feed(reader, BuildMessage(("SER#", "HQ123456"), ("V", "42"))));
        Assert.Equal("42", reader.LastFrame["V"]);
    }

    [Fact]
    public void ProcessInputByte_TwoInterleavedReaders_KeepIndependentState()
    {
        var firstReader = new VEDirectReader("/dev/ttyUSB0");
        var secondReader = new VEDirectReader("/dev/ttyUSB1");
        var firstMessage = BuildMessage(("SER#", "HQ111"), ("V", "13000"));
        var secondMessage = BuildMessage(("SER#", "HQ222"), ("V", "12500"));

        for (var index = 0; index < Math.Max(firstMessage.Length, secondMessage.Length); index++)
        {
            if (index < firstMessage.Length)
            {
                firstReader.ProcessInputByte(firstMessage[index]);
            }
            if (index < secondMessage.Length)
            {
                secondReader.ProcessInputByte(secondMessage[index]);
            }
        }

        Assert.Equal("HQ111", firstReader.LastFrame["SER#"]);
        Assert.Equal("13000", firstReader.LastFrame["V"]);
        Assert.Equal("HQ222", secondReader.LastFrame["SER#"]);
        Assert.Equal("12500", secondReader.LastFrame["V"]);
    }

    private static bool Feed(VEDirectReader reader, IEnumerable<byte> message)
    {
        var complete = false;
        foreach (var value in message)
        {
            complete = reader.ProcessInputByte(value);
        }
        return complete;
    }

    private static byte[] BuildMessage(params (string Key, string Value)[] fields)
    {
        var bytes = new List<byte>();
        foreach (var (key, value) in fields)
        {
            bytes.AddRange(Encoding.ASCII.GetBytes($"\r\n{key}\t{value}"));
        }

        bytes.AddRange(Encoding.ASCII.GetBytes("\r\nChecksum\t"));
        byte sum = 0;
        foreach (var value in bytes)
        {
            sum += value;
        }
        bytes.Add((byte)-sum);
        return [.. bytes];
    }
}
