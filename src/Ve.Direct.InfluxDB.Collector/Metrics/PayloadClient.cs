using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using InfluxDB.Collector.Diagnostics;
using InfluxDB.LineProtocol.Client;
using InfluxDB.LineProtocol.Payload;

namespace Ve.Direct.InfluxDB.Collector.Metrics
{
    public class PayloadClient
    {
        private readonly MetricsConfigurationModel configuration;
        private readonly LineProtocolClient client;
        
        private LineProtocolPayload payload;

        public PayloadClient(MetricsConfigurationModel configuration)
        {
            this.configuration = configuration;

            this.client = new LineProtocolClient(configuration.InfluxDbUri, configuration.InfluxDbName);
            this.payload = new LineProtocolPayload();

            CollectorLog.RegisterErrorHandler((message, exception) =>
            {
                Logger.Error($"Collector {message}: {exception}");
            });
        }

        public void AddPayload(MetricsTransmissionModel metrics)
        {
            var payloadDateTime = DateTime.UtcNow;

            this.payload.Add(new LineProtocolPoint($"{this.configuration.MetricPrefix}_battery",
                new Dictionary<string, object> { { "voltage", metrics.BatteryMillivolt }, { "current", metrics.BatteryMillicurrent }, { "power", metrics.BatteryPowerCalculated } },
                new Dictionary<string, string> { { "host", Environment.MachineName } }, payloadDateTime));

            this.payload.Add(new LineProtocolPoint($"{this.configuration.MetricPrefix}_panel",
                new Dictionary<string, object> { { "voltage", metrics.PanelMillivolt }, { "current", metrics.PanelMillicurrentCalculated }, { "power", metrics.PanelPower } },
                new Dictionary<string, string> { { "host", Environment.MachineName } }, payloadDateTime));

            this.payload.Add(new LineProtocolPoint($"{this.configuration.MetricPrefix}_load",
                new Dictionary<string, object> { { "current", metrics.LoadMillicurrent }, { "power", metrics.LoadPowerCalculated }, { "Status", metrics.LoadStatus } },
                new Dictionary<string, string> { { "host", Environment.MachineName } }, payloadDateTime));

            this.payload.Add(new LineProtocolPoint($"{this.configuration.MetricPrefix}_today",
                new Dictionary<string, object> { { "yield", metrics.TodayYield }, { "power", metrics.TodayPower } },
                new Dictionary<string, string> { { "host", Environment.MachineName } }, payloadDateTime));

            this.payload.Add(new LineProtocolPoint($"{this.configuration.MetricPrefix}_VICTRON",
                new Dictionary<string, object> { { "CS_Status", metrics.VICTRON_CS_Status }, { "ERR_Status", metrics.VICTRON_ERR_Status }, { "MPPT_Status", metrics.VICTRON_MPPT_Status } },
                new Dictionary<string, string> { { "host", Environment.MachineName } }, payloadDateTime));
        }

        public async Task TrySendPayload()
        {
            var influxResult = await this.client.WriteAsync(this.payload).ConfigureAwait(false);

            if (influxResult.Success)
            {
                this.payload = new LineProtocolPayload();

                Logger.Debug("InfluxDb write operation completed successfully");
            }
            else
            {
                Logger.Error(influxResult.ErrorMessage);
            }
        }
    }
}
