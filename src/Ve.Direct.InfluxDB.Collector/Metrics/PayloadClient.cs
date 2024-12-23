using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using InfluxDB.Client.Writes;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Ve.Direct.InfluxDB.Collector.Metrics
{
    public class PayloadClient
    {
        private readonly CollectorConfiguration configuration;
        private readonly InfluxDBClient influxDBClient;
        private readonly List<PointData> pointDataList;
        private DateTime lastTransmissionTime = DateTime.MinValue;

        public PayloadClient(CollectorConfiguration configuration)
        {
            this.configuration = configuration;
            this.pointDataList = [];

            var builder = new InfluxDBClientOptions.Builder();
            builder.Url(configuration.InfluxDbUrl);
            builder.Bucket(configuration.InfluxDbBucket);
            builder.Org(configuration.InfluxDbOrg);

            this.influxDBClient = new InfluxDBClient(builder.Build());
        }

        public void AddPayload(MetricsTransmissionModel metrics)
        {
            var payloadDateTime = DateTime.UtcNow;

            this.pointDataList.Add(PointData.Measurement($"{this.configuration.InfluxMetricPrefix}_battery")
                    .Tag("host", Environment.MachineName)
                    .Field("voltage", metrics.BatteryMillivolt)
                    .Field("current", metrics.BatteryMillicurrent)
                    .Field("power", metrics.BatteryMilliwattsCalculated)
                    .Timestamp(payloadDateTime, WritePrecision.Ms));

            this.pointDataList.Add(PointData.Measurement($"{this.configuration.InfluxMetricPrefix}_panel")
                    .Tag("host", Environment.MachineName)
                    .Field("voltage", metrics.PanelMillivolt)
                    .Field("current", metrics.PanelMillicurrentCalculated)
                    .Field("power", metrics.PanelPower)
                    .Timestamp(payloadDateTime, WritePrecision.Ms));

            this.pointDataList.Add(PointData.Measurement($"{this.configuration.InfluxMetricPrefix}_load")
                    .Tag("host", Environment.MachineName)
                    .Field("current", metrics.LoadMillicurrent)
                    .Field("power", metrics.LoadMilliwattsCalculated)
                    .Field("Status", metrics.LoadStatus)
                    .Timestamp(payloadDateTime, WritePrecision.Ms));

            this.pointDataList.Add(PointData.Measurement($"{this.configuration.InfluxMetricPrefix}_today")
                    .Tag("host", Environment.MachineName)
                    .Field("yield", metrics.TodayYield)
                    .Field("power", metrics.TodayPower)
                    .Timestamp(payloadDateTime, WritePrecision.Ms));

            this.pointDataList.Add(PointData.Measurement($"{this.configuration.InfluxMetricPrefix}_VICTRON")
                    .Tag("host", Environment.MachineName)
                    .Field("CS_Status", metrics.VICTRON_CS_Status)
                    .Field("ERR_Status", metrics.VICTRON_ERR_Status)
                    .Field("MPPT_Status", metrics.VICTRON_MPPT_Status)
                    .Timestamp(payloadDateTime, WritePrecision.Ms));
        }

        public async Task TrySendPayload()
        {
            var now = DateTime.Now;
            var pastSeconds = (now - this.lastTransmissionTime).TotalSeconds;

            if (pastSeconds >= this.configuration.Interval)
            {
                this.lastTransmissionTime = now;

                try
                {
                    var writeApi = this.influxDBClient.GetWriteApiAsync();
                    await writeApi.WritePointsAsync(this.pointDataList).ConfigureAwait(false);

                    ConsoleLogger.Debug("InfluxDb write operation completed successfully!");
                    ConsoleLogger.Debug($"{this.pointDataList.Count} data points were sent.");

                    this.pointDataList.Clear();
                }
                catch (Exception e)
                {
                    ConsoleLogger.Error(e);
                }
            }
        }
    }
}
