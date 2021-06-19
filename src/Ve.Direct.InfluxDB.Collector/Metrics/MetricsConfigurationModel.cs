using System;

namespace Ve.Direct.InfluxDB.Collector.Metrics
{
    public class MetricsConfigurationModel
    {
        public Uri InfluxDbUri { get; set; }

        public string InfluxDbName { get; set; }

        public int IntervalSeconds { get; set; }

        public string MetricPrefix { get; set; }

        public void Validate()
        {
            const int minInterval = 10;

            if (this.IntervalSeconds < minInterval)
            {
                throw new ArgumentException($"The {nameof(this.IntervalSeconds)} should be greater than {minInterval}");
            }
        }
    }
}
