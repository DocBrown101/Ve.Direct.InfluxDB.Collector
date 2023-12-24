using System;
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
                ConsoleLogger.Init(config.DebugOutput, "3.0.0");
                ConsoleLogger.Debug($"Current output setting: {config.Output}");

                try
                {
                    IReader reader = config.UseChecksums ? new VEDirectReaderWithChecksum(config.SerialPortName) : new VEDirectReader(config.SerialPortName);

                    switch (config.Output)
                    {
                        case CollectorConfiguration.OutputDefinition.Console:
                            reader.ReadSerialPortData(null, cancellationToken);
                            break;
                        case CollectorConfiguration.OutputDefinition.Influx:
                            var metricsCompositor = new MetricsCompositor(config);
                            reader.ReadSerialPortData(metricsCompositor.SendMetricsCallback, cancellationToken);
                            break;
                        default:
                            throw new InvalidEnumArgumentException(nameof(config.Output));
                    }

                    await Task.CompletedTask;
                }
                catch (Exception e)
                {
                    ConsoleLogger.Error(e);
                    Environment.Exit(1);
                }
            });

            return app.Execute(args);
        }
    }
}
