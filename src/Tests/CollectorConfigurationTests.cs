namespace Tests;

using McMaster.Extensions.CommandLineUtils;
using Ve.Direct.InfluxDB.Collector;
using Xunit;

public sealed class CollectorConfigurationTests
{
    [Fact]
    public void InfluxBatchOptions_Defaults_AreEventBased()
    {
        var (app, configuration) = CreateConfiguration();

        app.Execute(Array.Empty<string>());

        Assert.Equal(10, configuration.InfluxEventsPerWrite);
        Assert.Equal(10_000, configuration.InfluxMaxBufferedPoints);
    }

    [Fact]
    public void InfluxBatchOptions_ExplicitValues_AreParsed()
    {
        var (app, configuration) = CreateConfiguration();

        app.Execute("--influxEventsPerWrite", "25", "--influxMaxBufferedPoints", "5000");

        Assert.Equal(25, configuration.InfluxEventsPerWrite);
        Assert.Equal(5_000, configuration.InfluxMaxBufferedPoints);
    }

    private static (CommandLineApplication App, CollectorConfiguration Configuration) CreateConfiguration()
    {
        var app = new CommandLineApplication();
        var configuration = new CollectorConfiguration(app);
        app.OnExecute(() => 0);
        return (app, configuration);
    }
}
