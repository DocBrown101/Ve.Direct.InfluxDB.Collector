namespace Ve.Direct.InfluxDB.Collector.Metrics;

internal sealed class MetricsTransmissionModel
{
    internal MetricsTransmissionModel(string serialNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serialNumber);
        this.SerialNumber = serialNumber;
    }

    internal string SerialNumber { get; }

    internal long BatteryVoltageMillivolts { get; set; }

    internal long BatteryCurrentMilliamps { get; set; }

    internal long CalculatedBatteryPowerMilliwatts { get; private set; }

    internal long PanelVoltageMillivolts { get; set; }

    internal long PanelPowerWatts { get; set; }

    internal long CalculatedPanelCurrentMilliamps { get; private set; }

    internal long LoadCurrentMilliamps { get; set; }

    internal long CalculatedLoadPowerMilliwatts { get; private set; }

    internal int LoadState { get; set; }

    internal long TodayYieldWattHours { get; set; }

    internal long TodayMaximumPowerWatts { get; set; }

    internal int ChargerState { get; set; }

    internal int ErrorCode { get; set; }

    internal int TrackerState { get; set; }

    internal void CalculateMissingMetrics()
    {
        if (this.LoadState > 0 && this.BatteryCurrentMilliamps < 0)
        {
            this.LoadCurrentMilliamps = Math.Max(
                this.LoadCurrentMilliamps,
                Math.Abs(this.BatteryCurrentMilliamps));
        }

        if (this.BatteryVoltageMillivolts > 0 && this.BatteryCurrentMilliamps != 0)
        {
            this.CalculatedBatteryPowerMilliwatts =
                this.BatteryVoltageMillivolts * this.BatteryCurrentMilliamps / 1000;
        }

        if (this.PanelVoltageMillivolts > 0 && this.PanelPowerWatts > 0)
        {
            this.CalculatedPanelCurrentMilliamps = Convert.ToInt64(
                this.PanelPowerWatts / (decimal)this.PanelVoltageMillivolts * 1000 * 1000);
        }

        if (this.LoadCurrentMilliamps > 0 && this.BatteryVoltageMillivolts > 0)
        {
            this.CalculatedLoadPowerMilliwatts =
                this.BatteryVoltageMillivolts * this.LoadCurrentMilliamps / 1000;
        }
    }
}
