namespace Tests
{
    using Ve.Direct.InfluxDB.Collector.Metrics;
    using Xunit;

    public class MetricsTransmissionModelTests
    {
        [Fact]
        public void TestCalculateMissingData()
        {
            var model = new MetricsTransmissionModel
            {
                BatteryMillivolt = 12800,
                BatteryMillicurrent = 1660,
                LoadMillicurrent = 1660,
                PanelPower = 231,
                PanelMillivolt = 40000
            };
            model.CalculateMissingMetrics();

            Assert.Equal(21248, model.BatteryMilliwattsCalculated);
            Assert.Equal(21248, model.LoadMilliwattsCalculated);
            Assert.Equal(5775, model.PanelMillicurrentCalculated);
        }
    }
}
