namespace Ve.Direct.InfluxDB.Collector
{
    using McMaster.Extensions.CommandLineUtils;

    public class CollectorConfiguration
    {
        private readonly CommandOption<OutputDefinition> outputDefinition;
        private readonly CommandOption<int> interval;
        private readonly CommandOption<string> serialPortName;
        private readonly CommandOption<string> influxDbUrl;
        private readonly CommandOption<string> influxDbBucket;
        private readonly CommandOption<string> influxDbOrg;
        private readonly CommandOption<string> influxMetricPrefix;
        private readonly CommandOption<bool> useChecksums;
        private readonly CommandOption<bool> debugOutput;

        public OutputDefinition Output => this.outputDefinition.ParsedValue;

        public int Interval => this.interval.ParsedValue;

        public string SerialPortName => this.serialPortName.ParsedValue;

        public string InfluxDbUrl => this.influxDbUrl.ParsedValue;
        public string InfluxDbBucket => this.influxDbBucket.ParsedValue;
        public string InfluxDbOrg => this.influxDbOrg.ParsedValue;
        public string InfluxMetricPrefix => this.influxMetricPrefix.ParsedValue;

        public bool UseChecksums => this.useChecksums.ParsedValue;

        public bool DebugOutput => this.debugOutput.ParsedValue;

        public CollectorConfiguration(CommandLineApplication app)
        {
            this.outputDefinition = app.Option<OutputDefinition>("-o|--output", "Console or Influx", CommandOptionType.SingleValue);
            this.interval = app.Option<int>("-i|--interval", "Interval in seconds", CommandOptionType.SingleValue).Accepts(v => v.Range(10, 3600));
            this.serialPortName = app.Option<string>("-p|--port", "The name of the port to use. USB VE.Direct cable would be /dev/ttyUSB0", CommandOptionType.SingleValue);
            this.influxDbUrl = app.Option<string>("--influxDbUrl", "The InfluxDb Url", CommandOptionType.SingleValue);
            this.influxDbBucket = app.Option<string>("--influxDbBucket", "The InfluxDb Bucket name", CommandOptionType.SingleValue);
            this.influxDbOrg = app.Option<string>("--influxDbOrg", "The InfluxDb Org name", CommandOptionType.SingleValue);
            this.influxMetricPrefix = app.Option<string>("--influxMetricPrefix", "Used prefix for all metrics", CommandOptionType.SingleValue);
            this.useChecksums = app.Option<bool>("-c", "Use checksums?", CommandOptionType.SingleOrNoValue);
            this.debugOutput = app.Option<bool>("--debugOutput", "Debug?", CommandOptionType.SingleOrNoValue);

            this.outputDefinition.DefaultValue = OutputDefinition.Console;

            this.interval.DefaultValue = 30;

            this.influxDbUrl.DefaultValue = "http://192.168.0.220:8086";
            this.influxDbBucket.DefaultValue = "solar";
            this.influxDbOrg.DefaultValue = "home";
            this.influxMetricPrefix.DefaultValue = "ve_direct";
        }

        public enum OutputDefinition
        {
            Console,
            Influx
        }
    }
}
