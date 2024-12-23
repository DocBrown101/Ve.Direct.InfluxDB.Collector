using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using Ve.Direct.InfluxDB.Collector.Metrics;
using Ve.Direct.InfluxDB.Collector.ProtocolReader;

namespace Ve.Direct.InfluxDB.Collector
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            var app = new CommandLineApplication();
            var config = new CollectorConfiguration(app);
            app.HelpOption();
            app.OnExecuteAsync(async cancellationToken =>
            {
                ConsoleLogger.Init(config.DebugOutput, "3.2.0");
                ConsoleLogger.Debug($"Current output setting: {config.Output}");

                try
                {
                    IReader reader = config.UseChecksums ? new VEDirectReaderWithChecksum(config.SerialPortName) : new VEDirectReader(config.SerialPortName);

                    switch (config.Output)
                    {
                        case CollectorConfiguration.OutputDefinition.Console:
                            reader.ReadSerialPortData(WriteMetricsCallback, cancellationToken);
                            break;
                        case CollectorConfiguration.OutputDefinition.Influx:
                            var metricsCompositor = new MetricsCompositor(config);
                            reader.ReadSerialPortData(metricsCompositor.SendMetricsCallback, cancellationToken);
                            break;
                        default:
                            throw new InvalidEnumArgumentException(nameof(config.Output));
                    }

                    await Task.CompletedTask.ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    ConsoleLogger.Error(e);
                    Environment.Exit(1);
                }
            });

            return app.Execute(args);
        }

        private static void WriteMetricsCallback(Dictionary<string, string> serialData)
        {
            foreach (var kvp in serialData)
            {
                var outputValue = kvp.Key.Equals("pid", StringComparison.CurrentCultureIgnoreCase) ? kvp.Value.GetVictronDeviceNameByPid() : kvp.Value;
                Console.WriteLine("KeyValue: {0} - {1}", kvp.Key, outputValue);
            }
            Console.WriteLine("---");
        }
    }
}
