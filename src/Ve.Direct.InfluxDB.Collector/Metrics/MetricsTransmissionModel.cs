using System;

namespace Ve.Direct.InfluxDB.Collector.Metrics
{
    public class MetricsTransmissionModel
    {
        public long BatteryMillivolt { get; set; }

        public long BatteryMillicurrent { get; set; }

        public long BatteryPower { get; private set; }

        public long PanelMillivolt { get; set; }

        public long PanelPower { get; set; }

        public long PanelMillicurrent { get; private set; }

        public long LoadMillicurrent { get; set; }

        public long LoadPower { get; private set; }

        public void CalculateMissingData()
        {
            this.CalculateBatteryPower();
            this.CalculatePanelCurrent();
            this.CalculateLoadPower();
        }

        private void CalculateBatteryPower()
        {
            if (this.BatteryMillivolt > 0 && this.BatteryMillicurrent != 0)
            {
                this.BatteryPower = Convert.ToInt64(this.BatteryMillivolt * (decimal)this.BatteryMillicurrent / 1000 / 1000);
            }
        }

        private void CalculatePanelCurrent()
        {
            if (this.PanelMillivolt > 0 && this.PanelPower > 0)
            {
                this.PanelMillicurrent = Convert.ToInt64(this.PanelPower / (decimal)this.PanelMillivolt * 1000 * 1000);
            }
        }

        private void CalculateLoadPower()
        {
            if (this.LoadMillicurrent > 0 && this.BatteryMillivolt > 0)
            {
                this.LoadPower = this.BatteryMillivolt * this.LoadMillicurrent / 1000 / 1000;
            }
        }
    }
}
