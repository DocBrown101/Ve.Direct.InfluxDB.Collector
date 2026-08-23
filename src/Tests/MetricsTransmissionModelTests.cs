namespace Tests;

using Ve.Direct.InfluxDB.Collector.Metrics;
using Xunit;

public sealed class MetricsTransmissionModelTests
{
    [Fact]
    public void CalculateMissingMetrics_DerivesPowerAndPanelCurrent()
    {
        var model = new MetricsTransmissionModel("HQ123456")
        {
            BatteryVoltageMillivolts = 12800,
            BatteryCurrentMilliamps = 1660,
            LoadCurrentMilliamps = 1660,
            PanelPowerWatts = 231,
            PanelVoltageMillivolts = 40000
        };

        model.CalculateMissingMetrics();

        Assert.Equal(21248, model.CalculatedBatteryPowerMilliwatts);
        Assert.Equal(21248, model.CalculatedLoadPowerMilliwatts);
        Assert.Equal(5775, model.CalculatedPanelCurrentMilliamps);
    }
}
