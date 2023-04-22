namespace Tests
{
    using System;
    using Ve.Direct.InfluxDB.Collector;
    using Xunit;

    /// <summary>
    /// TODO: add more tests
    /// </summary>
    public class VEDirectReaderTests
    {
        [Fact]
        public void TestWaitHeaderState()
        {
            var parser = new VEDirectReader();
            var inputByte = Convert.ToByte('H');

            var result = parser.ProcessInputByte(inputByte);

            Assert.Null(result);
            //Assert.Equal(ReadState.WAIT_HEADER, parser.State);
        }
    }
}
