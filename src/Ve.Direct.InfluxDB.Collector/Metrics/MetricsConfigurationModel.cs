using System;

namespace Ve.Direct.InfluxDB.Collector.Metrics
{
    public class MetricsConfigurationModel
    {
        public string InfluxDbUrl { get; set; }

        public string InfluxDbBucket { get; set; }

        public string InfluxDbOrg { get; set; }

        public string MetricPrefix { get; set; }

        public int MinimumDataPoints { get; set; }

        public void Validate()
        {
            const int minInterval = 1;

            if (string.IsNullOrWhiteSpace(this.InfluxDbUrl))
            {
                throw new ArgumentNullException($"The {nameof(this.InfluxDbUrl)} must be set!");
            }

            if (string.IsNullOrWhiteSpace(this.InfluxDbBucket))
            {
                throw new ArgumentNullException($"The {nameof(this.InfluxDbBucket)} must be set!");
            }

            if (string.IsNullOrWhiteSpace(this.InfluxDbOrg))
            {
                throw new ArgumentNullException($"The {nameof(this.InfluxDbOrg)} must be set!");
            }

            if (string.IsNullOrWhiteSpace(this.MetricPrefix))
            {
                throw new ArgumentNullException($"The {nameof(this.MetricPrefix)} must be set!");
            }

            if (this.MinimumDataPoints < minInterval)
            {
                throw new ArgumentException($"The {nameof(this.MinimumDataPoints)} should be greater than {minInterval}");
            }
        }
    }
}
