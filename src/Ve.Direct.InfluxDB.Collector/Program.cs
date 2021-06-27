using System;
using System.Threading;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using Ve.Direct.InfluxDB.Collector.Metrics;

namespace Ve.Direct.InfluxDB.Collector
{
    public class Program
    {
        public static async Task<int> Main(string[] args) => await CommandLineApplication.ExecuteAsync<Program>(args);

        public enum OutputSettingEnum
        {
            Console,
            Influx
        }

        [Option(CommandOptionType.SingleValue, Description = "Console or Influx. Defaults to Console")]
        public OutputSettingEnum OutputSetting { get; set; }

        [Option("--influxDbUri", Description = "The InfluxDb Uri. E.g. http://192.168.0.220:8086")]
        public Uri InfluxDbUri { get; } = new Uri("http://192.168.0.220:8086");

        [Option("--influxDbName", Description = "The InfluxDb database name. Defaults to solar")]
        public string InfluxDbName { get; } = "solar";

        [Option("--metricPrefix", Description = "Prefix all metrics pushed into the InfluxDb. Defaults to ve_direct")]
        public string MetricPrefix { get; } = "ve_direct";

        [Option("--interval", Description = "The interval in seconds to request metrics. Defaults to 10")]
        public int IntervalSeconds { get; } = 10;

        [Option("--debugOutput", Description = "By default it is disabled.")]
        public bool DebugOutput { get; } = false;

        private async Task OnExecuteAsync(CancellationToken ct)
        {
            if (this.DebugOutput)
            {
                Logger.EnableDebugOutput();
            }

            try
            {
                var config = new MetricsConfigurationModel()
                {
                    InfluxDbUri = InfluxDbUri,
                    IntervalSeconds = IntervalSeconds,
                    InfluxDbName = InfluxDbName,
                    MetricPrefix = MetricPrefix
                };
                config.Validate();

                var version = "1.1.0";
                Logger.Info($"Current Version: {version}");
                Logger.Debug($"Current output setting: {this.OutputSetting}");
                Logger.Debug($"InfluxDb {config.InfluxDbUri}");
                Logger.Debug($"Interval {config.IntervalSeconds} seconds");
                Logger.Info($"Collect Metrics ...");

                var reader = new VEDirectReader();

                switch (this.OutputSetting)
                {
                    case OutputSettingEnum.Console:
                        reader.ReadPortData(reader.PrintMetricsCallback, ct);
                        break;
                    case OutputSettingEnum.Influx:
                        var metricsCompositor = new MetricsCompositor(config);
                        await reader.ReadPortData(metricsCompositor.SendMetricsCallback, ct);
                        break;
                    default:
                        throw new System.ComponentModel.InvalidEnumArgumentException(nameof(this.OutputSetting));
                }
            }
            catch (Exception e)
            {
                Logger.Error(e.Message);
                Environment.Exit(1);
            }
        }
    }
}
