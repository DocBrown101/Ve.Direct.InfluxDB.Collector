namespace Tests;

using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Xunit;

/// <summary>
/// Shared test suite for all Ve.Direct text-protocol byte readers.
/// Concrete subclasses supply CreateReader(), ProcessByte(), and GetData().
/// </summary>
public abstract class VEDirectReaderBaseTests<TReader> where TReader : class
{
    protected abstract TReader CreateReader();
    protected abstract bool ProcessByte(TReader reader, byte b);

    protected virtual Dictionary<string, string> GetData(TReader reader)
    {
        var field = typeof(TReader).GetField("serialData", BindingFlags.NonPublic | BindingFlags.Instance);
        return (Dictionary<string, string>)field!.GetValue(reader)!;
    }

    /// <summary>
    /// Builds a valid Ve.Direct text protocol message from key-value pairs.
    /// Format: \r\nKey\tValue\r\n...\r\nChecksum\t{byte}
    /// The checksum byte is chosen so that the sum of all bytes in the message equals 0 mod 256.
    /// </summary>
    protected static byte[] BuildMessage(params (string key, string value)[] fields)
    {
        var bytes = new List<byte>();
        foreach (var (key, value) in fields)
        {
            bytes.Add(0x0D); // \r
            bytes.Add(0x0A); // \n
            bytes.AddRange(Encoding.ASCII.GetBytes(key));
            bytes.Add(0x09); // \t
            bytes.AddRange(Encoding.ASCII.GetBytes(value));
        }
        bytes.Add(0x0D);
        bytes.Add(0x0A);
        bytes.AddRange(Encoding.ASCII.GetBytes("Checksum"));
        bytes.Add(0x09);

        byte sum = 0;
        foreach (var b in bytes)
            sum += b;
        bytes.Add((byte)-sum); // wraps: (256 - sum) % 256

        return [.. bytes];
    }

    private bool FeedMessage(TReader reader, byte[] message)
    {
        var result = false;
        foreach (var b in message)
            result = this.ProcessByte(reader, b);
        return result;
    }

    [Fact]
    public void Constructor_WithPortName_DoesNotThrow()
    {
        var reader = this.CreateReader();
        Assert.NotNull(reader);
    }

    [Fact]
    public void ProcessInputByte_InWaitHeaderState_ReturnsFalse()
    {
        var reader = this.CreateReader();

        Assert.False(this.ProcessByte(reader, (byte)'A'));
        Assert.False(this.ProcessByte(reader, 0x0D)); // \r stays in WAIT_HEADER
    }

    [Fact]
    public void ProcessInputByte_PartialMessage_NeverReturnsTrue()
    {
        var reader = this.CreateReader();

        // \r\nV\t13310\r — key-value stored, but no Checksum yet
        byte[] partial = [0x0D, 0x0A, .. Encoding.ASCII.GetBytes("V"), 0x09, .. Encoding.ASCII.GetBytes("13310"), 0x0D];
        foreach (var b in partial)
            Assert.False(this.ProcessByte(reader, b));
    }

    [Fact]
    public void ProcessInputByte_CompleteValidMessage_ReturnsTrue()
    {
        var reader = this.CreateReader();
        Assert.True(this.FeedMessage(reader, BuildMessage(("V", "13310"))));
    }

    [Fact]
    public void ProcessInputByte_OnlyChecksumByteReturnsTrue()
    {
        var reader = this.CreateReader();
        var message = BuildMessage(("V", "13310"));

        for (var i = 0; i < message.Length - 1; i++)
            Assert.False(this.ProcessByte(reader, message[i]));

        Assert.True(this.ProcessByte(reader, message[^1]));
    }

    [Fact]
    public void ProcessInputByte_InvalidChecksum_ReturnsFalse()
    {
        var reader = this.CreateReader();
        var message = BuildMessage(("V", "13310"));
        message[^1] ^= 0x01; // corrupt checksum byte

        Assert.False(this.FeedMessage(reader, message));
    }

    [Fact]
    public void ProcessInputByte_SingleKeyValue_StoredInDictionary()
    {
        var reader = this.CreateReader();
        this.FeedMessage(reader, BuildMessage(("V", "13310")));

        var data = this.GetData(reader);
        Assert.Equal("13310", data["V"]);
    }

    [Fact]
    public void ProcessInputByte_MultipleKeyValues_AllStoredInDictionary()
    {
        var reader = this.CreateReader();
        this.FeedMessage(reader, BuildMessage(("PID", "0xA060"), ("V", "13310"), ("I", "500"), ("CS", "3")));

        var data = this.GetData(reader);
        Assert.Equal("0xA060", data["PID"]);
        Assert.Equal("13310", data["V"]);
        Assert.Equal("500", data["I"]);
        Assert.Equal("3", data["CS"]);
    }

    [Fact]
    public void ProcessInputByte_DuplicateKey_ValueUpdated()
    {
        var reader = this.CreateReader();
        this.FeedMessage(reader, BuildMessage(("V", "100")));
        this.FeedMessage(reader, BuildMessage(("V", "200")));

        Assert.Equal("200", this.GetData(reader)["V"]);
    }

    [Fact]
    public void ProcessInputByte_HexFrameBeforeTextFrame_TextFrameProcessedCorrectly()
    {
        var reader = this.CreateReader();

        // A hex frame ":A12\n" resets the running checksum and returns to WAIT_HEADER
        foreach (var b in Encoding.ASCII.GetBytes(":A12\n"))
            this.ProcessByte(reader, b);

        // After the hex frame the checksum accumulator is 0 — BuildMessage checksum is valid
        Assert.True(this.FeedMessage(reader, BuildMessage(("V", "42"))));
        Assert.Equal("42", this.GetData(reader)["V"]);
    }

    [Fact]
    public void ProcessInputByte_RealisticFullBlock_AllFieldsParsed()
    {
        var reader = this.CreateReader();
        this.FeedMessage(reader, BuildMessage(
            ("PID", "0xA060"),
            ("FW", "161"),
            ("V", "13310"),
            ("I", "0"),
            ("VPV", "70"),
            ("PPV", "0"),
            ("CS", "0"),
            ("MPPT", "0"),
            ("ERR", "0"),
            ("LOAD", "ON"),
            ("IL", "0"),
            ("HSDS", "88")
        ));

        var data = this.GetData(reader);
        Assert.Equal("0xA060", data["PID"]);
        Assert.Equal("161", data["FW"]);
        Assert.Equal("13310", data["V"]);
        Assert.Equal("ON", data["LOAD"]);
        Assert.Equal("88", data["HSDS"]);
    }
}
