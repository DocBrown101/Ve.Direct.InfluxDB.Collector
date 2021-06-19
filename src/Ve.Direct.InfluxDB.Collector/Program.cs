using System;
using System.Threading;
using McMaster.Extensions.CommandLineUtils;
using Ve.Direct.InfluxDB.Collector.Metrics;

namespace Ve.Direct.InfluxDB.Collector
{
    public class Program
    {
        private static bool keepRunning = true;

        public static int Main(string[] args)
        {
            return CommandLineApplication.Execute<Program>(args);
        }

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

        private void OnExecute()
        {
            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                keepRunning = false;
            };

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

                var version = "1.0.0";
                Logger.Info($"Current Version: {version}");
                Logger.Debug($"Current output setting: {this.OutputSetting}");
                Logger.Debug($"InfluxDb {config.InfluxDbUri}");
                Logger.Debug($"Interval {config.IntervalSeconds} seconds");
                Logger.Info($"Collect Metrics ...");

                var reader = new VEDirectReader();

                switch (this.OutputSetting)
                {
                    case OutputSettingEnum.Console:
                        reader.ReadPortData(reader.PrintMetricsCallback, ref keepRunning);
                        break;
                    case OutputSettingEnum.Influx:
                        var client = new MetricsClient(config);
                        reader.ReadPortData(client.SendMetricsCallback, ref keepRunning);
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
            finally
            {
                Thread.Sleep(100);
            }
        }
    }
}
