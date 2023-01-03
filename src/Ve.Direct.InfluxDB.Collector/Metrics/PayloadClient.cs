using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using InfluxDB.Client.Writes;
using System;
using System.Collections.Generic;

namespace Ve.Direct.InfluxDB.Collector.Metrics
{
    public class PayloadClient
    {
        private readonly MetricsConfigurationModel configuration;
        private readonly InfluxDBClient influxDBClient;
        private readonly List<PointData> pointDataList;

        public PayloadClient(MetricsConfigurationModel configuration)
        {
            this.configuration = configuration;
            this.pointDataList = new List<PointData>();

            var builder = new InfluxDBClientOptions.Builder();
            builder.Url(configuration.InfluxDbUrl);
            builder.Bucket(configuration.InfluxDbBucket);
            builder.Org(configuration.InfluxDbOrg);

            this.influxDBClient = new InfluxDBClient(builder.Build());
        }

        public void AddPayload(MetricsTransmissionModel metrics)
        {
            var payloadDateTime = DateTime.UtcNow;

            this.pointDataList.Add(PointData.Measurement($"{this.configuration.MetricPrefix}_battery")
                    .Tag("host", Environment.MachineName)
                    .Field("voltage", metrics.BatteryMillivolt)
                    .Field("current", metrics.BatteryMillicurrent)
                    .Field("power", metrics.BatteryPowerCalculated)
                    .Timestamp(payloadDateTime, WritePrecision.Ms));

            this.pointDataList.Add(PointData.Measurement($"{this.configuration.MetricPrefix}_panel")
                    .Tag("host", Environment.MachineName)
                    .Field("voltage", metrics.PanelMillivolt)
                    .Field("current", metrics.PanelMillicurrentCalculated)
                    .Field("power", metrics.PanelPower)
                    .Timestamp(payloadDateTime, WritePrecision.Ms));

            this.pointDataList.Add(PointData.Measurement($"{this.configuration.MetricPrefix}_load")
                    .Tag("host", Environment.MachineName)
                    .Field("current", metrics.LoadMillicurrent)
                    .Field("power", metrics.LoadPowerCalculated)
                    .Field("Status", metrics.LoadStatus)
                    .Timestamp(payloadDateTime, WritePrecision.Ms));

            this.pointDataList.Add(PointData.Measurement($"{this.configuration.MetricPrefix}_today")
                    .Tag("host", Environment.MachineName)
                    .Field("yield", metrics.TodayYield)
                    .Field("power", metrics.TodayPower)
                    .Timestamp(payloadDateTime, WritePrecision.Ms));

            this.pointDataList.Add(PointData.Measurement($"{this.configuration.MetricPrefix}_VICTRON")
                    .Tag("host", Environment.MachineName)
                    .Field("CS_Status", metrics.VICTRON_CS_Status)
                    .Field("ERR_Status", metrics.VICTRON_ERR_Status)
                    .Field("MPPT_Status", metrics.VICTRON_MPPT_Status)
                    .Timestamp(payloadDateTime, WritePrecision.Ms));
        }

        public void TrySendPayload()
        {
            if (this.pointDataList.Count >= (this.configuration.MinimumDataPoints * 5))
            {
                try
                {
                    using (var writeApi = this.influxDBClient.GetWriteApi())
                    {
                        writeApi.WritePoints(this.pointDataList);
                        Logger.Debug("InfluxDb write operation completed successfully!");
                        Logger.Debug($"{this.pointDataList.Count} data points were sent.");

                        this.pointDataList.Clear();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex.Message);
                }
            }
        }
    }
}
