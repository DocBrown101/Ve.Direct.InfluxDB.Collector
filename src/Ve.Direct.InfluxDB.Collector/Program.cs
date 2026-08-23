using System.Runtime.InteropServices;
using System.Text;
using McMaster.Extensions.CommandLineUtils;
using Ve.Direct.InfluxDB.Collector.Metrics;
using Ve.Direct.InfluxDB.Collector.ProtocolReader;
using Ve.Direct.InfluxDB.Collector.SerialPorts;

namespace Ve.Direct.InfluxDB.Collector;

public static class Program
{
    public static Task<int> Main(string[] args)
    {
        var app = new CommandLineApplication();
        var configuration = new CollectorConfiguration(app);
        app.HelpOption();
        app.OnExecuteAsync(cancellationToken => RunAsync(configuration, cancellationToken));
        return app.ExecuteAsync(args);
    }

    private static async Task<int> RunAsync(CollectorConfiguration configuration, CancellationToken cancellationToken)
    {
        ConsoleLogger.Init(configuration.DebugOutput, "4.0.0");
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        PosixSignalRegistration? terminationRegistration = null;
        if (!OperatingSystem.IsWindows())
        {
            terminationRegistration = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
            {
                context.Cancel = true;
                shutdown.Cancel();
            });
        }

        try
        {
            var scanInterval = TimeSpan.FromSeconds(configuration.ScanInterval);
            ConsoleLogger.Info("Starting VE.Direct collection with automatic port discovery.");

            if (configuration.Output == CollectorConfiguration.OutputDefinition.Influx)
            {
                using var metrics = new MetricsCompositor(configuration);
                var manager = new VEDirectDeviceManager(metrics.SendMetricsAsync, scanInterval);
                await manager.RunAsync(shutdown.Token).ConfigureAwait(false);
            }
            else
            {
                var manager = new VEDirectDeviceManager(WriteMetricsAsync, scanInterval);
                await manager.RunAsync(shutdown.Token).ConfigureAwait(false);
            }

            return 0;
        }
        catch (Exception exception)
        {
            ConsoleLogger.Error(exception);
            return 1;
        }
        finally
        {
            terminationRegistration?.Dispose();
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private static Task WriteMetricsAsync(
        string portName,
        IReadOnlyDictionary<string, string> frame,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var output = new StringBuilder().AppendLine($"Device: {frame["SER#"]} ({portName})");
        foreach (var (key, value) in frame)
        {
            var outputValue = key.Equals("PID", StringComparison.OrdinalIgnoreCase)
                ? value.GetVictronDeviceNameByPid()
                : value;
            output.AppendLine($"KeyValue: {key} - {outputValue}");
        }
        output.AppendLine("---");
        Console.Write(output);
        return Task.CompletedTask;
    }
}
