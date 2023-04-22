using System;
using System.Threading;
using McMaster.Extensions.CommandLineUtils;
using Ve.Direct.InfluxDB.Collector.Metrics;

namespace Ve.Direct.InfluxDB.Collector
{
    public class Program
    {
        public static void Main(string[] args) => CommandLineApplication.Execute<Program>(args);

        public enum OutputDefinition
        {
            Console,
            Influx
        }

        [Option("-o|--output", CommandOptionType.SingleValue, Description = "Console or Influx. Defaults to Console")]
        public OutputDefinition OutputSetting { get; set; }

        [Option("--influxDbUrl", Description = "The InfluxDb Url. E.g. http://192.168.0.220:8086")]
        public string InfluxDbUrl { get; } = "http://192.168.0.220:8086";

        [Option("--influxDbBucket", Description = "The InfluxDb Bucket name. Defaults to solar")]
        public string InfluxDbBucket { get; } = "solar";

        [Option("--influxDbOrg", Description = "The InfluxDb Org name. Defaults to home")]
        public string InfluxDbOrg { get; } = "home";

        [Option("--metricPrefix", Description = "Prefix all metrics pushed into the InfluxDb. Defaults to ve_direct")]
        public string MetricPrefix { get; } = "ve_direct";

        [Option("--interval", Description = "Minimum number of data points for transmission. Defaults to 10")]
        public int MinimumDataPoints { get; } = 10;

        [Option("--debugOutput", Description = "By default it is disabled.")]
        public bool DebugOutput { get; } = false;

        private void OnExecute(CancellationToken ct)
        {
            ConsoleLogger.Init(this.DebugOutput, "2.2.1");
            ConsoleLogger.Debug($"Current output setting: {this.OutputSetting}");

            try
            {
                var config = new MetricsConfigurationModel()
                {
                    InfluxDbUrl = InfluxDbUrl,
                    MinimumDataPoints = MinimumDataPoints,
                    InfluxDbBucket = InfluxDbBucket,
                    InfluxDbOrg = InfluxDbOrg,
                    MetricPrefix = MetricPrefix
                };
                config.Validate();

                var reader = new VEDirectReader();

                switch (this.OutputSetting)
                {
                    case OutputDefinition.Console:
                        reader.ReadSerialPortData(null, ct);
                        break;
                    case OutputDefinition.Influx:
                        var metricsCompositor = new MetricsCompositor(config);
                        reader.ReadSerialPortData(metricsCompositor.SendMetricsCallback, ct);
                        break;
                    default:
                        throw new System.ComponentModel.InvalidEnumArgumentException(nameof(this.OutputSetting));
                }
            }
            catch (Exception e)
            {
                ConsoleLogger.Error(e);
                Environment.Exit(1);
            }
        }
    }
}
