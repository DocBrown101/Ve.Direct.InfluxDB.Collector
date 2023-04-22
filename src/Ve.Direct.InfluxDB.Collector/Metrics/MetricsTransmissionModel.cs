using System;

namespace Ve.Direct.InfluxDB.Collector.Metrics
{
    public class MetricsTransmissionModel
    {
        public long BatteryMillivolt { get; set; }

        public long BatteryMillicurrent { get; set; }

        public long BatteryMilliwattsCalculated { get; private set; }

        public long PanelMillivolt { get; set; }

        public long PanelPower { get; set; }

        public long PanelMillicurrentCalculated { get; private set; }

        public long LoadMillicurrent { get; set; }

        public long LoadMilliwattsCalculated { get; private set; }

        public int LoadStatus { get; set; }

        public long TodayYield { get; set; }

        public long TodayPower { get; set; }

        public int VICTRON_CS_Status { get; set; }

        public int VICTRON_ERR_Status { get; set; }

        public int VICTRON_MPPT_Status { get; set; }

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
                this.BatteryMilliwattsCalculated = this.BatteryMillivolt * this.BatteryMillicurrent / 1000;
            }
        }

        private void CalculatePanelCurrent()
        {
            if (this.PanelMillivolt > 0 && this.PanelPower > 0)
            {
                this.PanelMillicurrentCalculated = Convert.ToInt64(this.PanelPower / (decimal)this.PanelMillivolt * 1000 * 1000);
            }
        }

        private void CalculateLoadPower()
        {
            if (this.LoadMillicurrent > 0 && this.BatteryMillivolt > 0)
            {
                this.LoadMilliwattsCalculated = this.BatteryMillivolt * this.LoadMillicurrent / 1000;
            }
        }
    }
}
