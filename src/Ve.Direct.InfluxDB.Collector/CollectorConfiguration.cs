using McMaster.Extensions.CommandLineUtils;

namespace Ve.Direct.InfluxDB.Collector;

internal sealed class CollectorConfiguration
{
    private readonly CommandOption<OutputDefinition> outputDefinition;
    private readonly CommandOption<int> scanInterval;
    private readonly CommandOption<bool> calculateMissingMetrics;
    private readonly CommandOption<bool> debugOutput;
    private readonly CommandOption<string> influxDbUrl;
    private readonly CommandOption<string> influxDbBucket;
    private readonly CommandOption<string> influxDbOrg;
    private readonly CommandOption<string> influxMetricPrefix;

    internal CollectorConfiguration(CommandLineApplication app)
    {
        this.outputDefinition = app.Option<OutputDefinition>(
            "-o|--output",
            "Console or Influx",
            CommandOptionType.SingleValue);
        this.scanInterval = app.Option<int>(
                "--scan-interval",
                "Serial port discovery interval in seconds",
                CommandOptionType.SingleValue)
            .Accepts(value => value.Range(1, 300));
        this.calculateMissingMetrics = app.Option<bool>("-m", "Calculate missing metrics?", CommandOptionType.NoValue);
        app.Option<bool>("-c", "Compatibility option; VE.Direct checksums are always validated", CommandOptionType.NoValue);
        this.debugOutput = app.Option<bool>("--debugOutput", "Enable debug output", CommandOptionType.NoValue);

        this.influxDbUrl = app.Option<string>(
            "--influxDbUrl",
            "The InfluxDB URL",
            CommandOptionType.SingleValue);
        this.influxDbBucket = app.Option<string>(
            "--influxDbBucket",
            "The InfluxDB bucket name",
            CommandOptionType.SingleValue);
        this.influxDbOrg = app.Option<string>(
            "--influxDbOrg",
            "The InfluxDB organization name",
            CommandOptionType.SingleValue);
        this.influxMetricPrefix = app.Option<string>(
            "--influxMetricPrefix",
            "Prefix for all metrics",
            CommandOptionType.SingleValue);

        this.SetDefaultValues();
    }

    internal OutputDefinition Output => this.outputDefinition.ParsedValue;

    internal int ScanInterval => this.scanInterval.ParsedValue;

    internal bool CalculateMissingMetrics => this.calculateMissingMetrics.ParsedValue;

    internal bool DebugOutput => this.debugOutput.ParsedValue;

    internal string InfluxDbUrl => this.influxDbUrl.ParsedValue;

    internal string InfluxDbBucket => this.influxDbBucket.ParsedValue;

    internal string InfluxDbOrg => this.influxDbOrg.ParsedValue;

    internal string InfluxMetricPrefix => this.influxMetricPrefix.ParsedValue;

    internal enum OutputDefinition
    {
        Console,
        Influx
    }

    private void SetDefaultValues()
    {
        this.outputDefinition.DefaultValue = OutputDefinition.Console;
        this.scanInterval.DefaultValue = 5;
        this.calculateMissingMetrics.DefaultValue = true;
        this.debugOutput.DefaultValue = false;

#pragma warning disable S5332
        this.influxDbUrl.DefaultValue = "http://192.168.0.220:8086";
#pragma warning restore S5332
        this.influxDbBucket.DefaultValue = "solar";
        this.influxDbOrg.DefaultValue = "home";
        this.influxMetricPrefix.DefaultValue = "ve_direct";
    }
}
