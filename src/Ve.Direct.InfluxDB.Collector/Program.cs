using System.Runtime.InteropServices;
using McMaster.Extensions.CommandLineUtils;
using Ve.Direct.InfluxDB.Collector.SerialPorts;

namespace Ve.Direct.InfluxDB.Collector;

public static class Program
{
    public static Task<int> Main(string[] args)
    {
        var app = new CommandLineApplication();
        var configuration = new CollectorConfiguration(app);
        app.HelpOption();
        app.OnExecuteAsync(cancellationToken => RunHostAsync(configuration, cancellationToken));
        return app.ExecuteAsync(args);
    }

    private static async Task<int> RunHostAsync(
        CollectorConfiguration configuration,
        CancellationToken cancellationToken)
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
            using var frameOutput = CreateFrameOutput(configuration, shutdown.Token);
            var portMonitor = new VEDirectPortMonitor(frameOutput.WriteFrameAsync, scanInterval);
            await portMonitor.MonitorPortsAsync(shutdown.Token).ConfigureAwait(false);

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

    private static IFrameOutput CreateFrameOutput(
        CollectorConfiguration configuration,
        CancellationToken writerCancellationToken)
    {
        return configuration.Output switch
        {
            CollectorConfiguration.OutputDefinition.Console => new ConsoleFrameOutput(),
            CollectorConfiguration.OutputDefinition.Influx => new InfluxFrameOutput(
                configuration,
                writerCancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(configuration), configuration.Output, "Unknown output.")
        };
    }
}
