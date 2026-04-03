namespace Tests;

using Ve.Direct.InfluxDB.Collector.ProtocolReader;

public class VEDirectReaderTests : VEDirectReaderBaseTests<VEDirectReader>
{
    protected override VEDirectReader CreateReader()
    {
        return new("TESTPORT");
    }

    protected override bool ProcessByte(VEDirectReader reader, byte b)
    {
        return reader.ProcessInputByte(b);
    }
}
