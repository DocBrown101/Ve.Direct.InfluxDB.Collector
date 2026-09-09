namespace Tests;

using McMaster.Extensions.CommandLineUtils;
using Ve.Direct.InfluxDB.Collector;
using Xunit;

public sealed class CollectorConfigurationTests
{
    [Fact]
    public void InfluxBatchOptions_Defaults_AreTimeBased()
    {
        var (app, configuration) = CreateConfiguration();

        app.Execute(Array.Empty<string>());

        Assert.Equal(10, configuration.InfluxWriteInterval);
        Assert.Equal(2_000, configuration.InfluxMaxQueuedFrames);
    }

    [Fact]
    public void InfluxBatchOptions_ExplicitValues_AreParsed()
    {
        var (app, configuration) = CreateConfiguration();

        app.Execute("--influxWriteInterval", "25", "--influxMaxQueuedFrames", "5000");

        Assert.Equal(25, configuration.InfluxWriteInterval);
        Assert.Equal(5_000, configuration.InfluxMaxQueuedFrames);
    }

    [Fact]
    public void CalculateMissingMetrics_MFlag_DisablesCalculation()
    {
        var (app, configuration) = CreateConfiguration();

        app.Execute(Array.Empty<string>());
        Assert.True(configuration.CalculateMissingMetrics);

        app.Execute("-m");
        Assert.False(configuration.CalculateMissingMetrics);
    }

    private static (CommandLineApplication App, CollectorConfiguration Configuration) CreateConfiguration()
    {
        var app = new CommandLineApplication();
        var configuration = new CollectorConfiguration(app);
        app.OnExecute(() => 0);
        return (app, configuration);
    }
}
