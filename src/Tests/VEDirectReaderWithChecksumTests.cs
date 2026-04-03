namespace Tests;

using Ve.Direct.InfluxDB.Collector.ProtocolReader;

public class VEDirectReaderWithChecksumTests : VEDirectReaderBaseTests<VEDirectReaderWithChecksum>
{
    protected override VEDirectReaderWithChecksum CreateReader()
    {
        return new("TESTPORT");
    }

    protected override bool ProcessByte(VEDirectReaderWithChecksum reader, byte b)
    {
        return reader.ProcessInputByte(b);
    }
}
