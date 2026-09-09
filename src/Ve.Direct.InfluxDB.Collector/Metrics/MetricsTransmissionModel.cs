namespace Ve.Direct.InfluxDB.Collector.Metrics;

internal sealed class MetricsTransmissionModel
{
    internal MetricsTransmissionModel(string serialNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serialNumber);
        this.SerialNumber = serialNumber;
    }

    internal string SerialNumber { get; }

    internal long? BatteryVoltageMillivolts { get; set; }

    internal long? BatteryCurrentMilliamps { get; set; }

    internal long? CalculatedBatteryPowerMilliwatts { get; private set; }

    internal long? PanelVoltageMillivolts { get; set; }

    internal long? PanelPowerWatts { get; set; }

    internal long? CalculatedPanelCurrentMilliamps { get; private set; }

    internal long? LoadCurrentMilliamps { get; set; }

    internal long? CalculatedLoadPowerMilliwatts { get; private set; }

    internal int? LoadState { get; set; }

    internal long? TodayYieldWattHours { get; set; }

    internal long? TodayMaximumPowerWatts { get; set; }

    internal int? ChargerState { get; set; }

    internal int? ErrorCode { get; set; }

    internal int? TrackerState { get; set; }

    internal void CalculateMissingMetrics()
    {
        if (this.LoadCurrentMilliamps is null
            && this.LoadState > 0
            && this.BatteryCurrentMilliamps < 0)
        {
            this.LoadCurrentMilliamps = Math.Abs(this.BatteryCurrentMilliamps.Value);
        }

        if (this.BatteryVoltageMillivolts > 0 && this.BatteryCurrentMilliamps is not null)
        {
            this.CalculatedBatteryPowerMilliwatts =
                this.BatteryVoltageMillivolts.Value * this.BatteryCurrentMilliamps.Value / 1000;
        }

        if (this.PanelVoltageMillivolts > 0 && this.PanelPowerWatts is not null)
        {
            this.CalculatedPanelCurrentMilliamps = Convert.ToInt64(
                this.PanelPowerWatts.Value / (decimal)this.PanelVoltageMillivolts.Value * 1000 * 1000);
        }

        if (this.LoadCurrentMilliamps is not null && this.BatteryVoltageMillivolts > 0)
        {
            this.CalculatedLoadPowerMilliwatts =
                this.BatteryVoltageMillivolts.Value * this.LoadCurrentMilliamps.Value / 1000;
        }
    }
}
