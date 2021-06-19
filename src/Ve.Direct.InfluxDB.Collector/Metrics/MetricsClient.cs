using System;
using System.Collections.Generic;
using InfluxDB.Collector;
using InfluxDB.Collector.Diagnostics;

namespace Ve.Direct.InfluxDB.Collector.Metrics
{
    /// <summary>
    /// VICTRON_MPPT
    /// '0': 'Off'
    /// '1': 'Limited'
    /// '2': 'Active'
    /// 
    /// VICTRON_CS
    /// '0': 'Off'
    /// '2': 'Fault'
    /// '3': 'Bulk'
    /// '4': 'Absorption'
    /// '5': 'Float'
    /// '7': 'Equalize (manual)'
    /// '245': 'Starting-up'
    /// '247': 'Auto equalize / Recondition'
    /// '252': 'External control'
    /// 
    /// VICTRON_ERR
    /// '0': 'No error'
    /// '2': 'Battery voltage too high'
    /// '17': 'Charger temperature too high'
    /// '18': 'Charger over current'
    /// '19': 'Charger current reversed'
    /// '20': 'Bulk time limit exceeded'
    /// '21': 'Current sensor issue'
    /// '26': 'Terminals overheated'
    /// '28': 'Converter issue',  # (dual converter models only)
    /// '33': 'Input voltage too high (solar panel)'
    /// '34': 'Input current too high (solar panel)'
    /// '38': 'Input shutdown (excessive battery voltage)'
    /// '39': 'Input shutdown (due to current flow during off mode)'
    /// '65': 'Lost communication with one of devices'
    /// '66': 'Synchronised charging device configuration issue'
    /// '67': 'BMS connection lost'
    /// '68': 'Network misconfigured'
    /// '116': 'Factory calibration data lost'
    /// '117': 'Invalid/incompatible firmware'
    /// '119': 'User settings invalid'
    /// </summary>
    public class MetricsClient
    {
        private readonly MetricsConfigurationModel configuration;

        private DateTime lastWriteTime = DateTime.MinValue;

        public MetricsClient(MetricsConfigurationModel configuration)
        {
            this.configuration = configuration;

            CollectorLog.RegisterErrorHandler((message, exception) =>
            {
                Logger.Error($"Collector {message}: {exception}");
            });
        }

        public void SendMetricsCallback(Dictionary<string, string> data)
        {
            if ((DateTime.Now - this.lastWriteTime).TotalSeconds >= this.configuration.IntervalSeconds)
            {
                this.lastWriteTime = DateTime.Now;
                var metrics = this.CreateMetricsCollector();
                var transmissionMetrics = new MetricsTransmissionModel();

                foreach (var kvp in data)
                {
                    switch (kvp.Key)
                    {
                        case "V": // Battery voltage (mV)
                            transmissionMetrics.BatteryMillivolt = this.ToLong(kvp.Value);
                            break;
                        case "I": // Battery current (mA)
                            transmissionMetrics.BatteryMillicurrent = this.ToLong(kvp.Value);
                            break;
                        case "VPV": // Panel voltage (mV)
                            transmissionMetrics.PanelMillivolt = this.ToLong(kvp.Value);
                            break;
                        case "PPV": // Panel Power (W)
                            transmissionMetrics.PanelPower = this.ToLong(kvp.Value);
                            break;
                        case "IL": // Load Current (mA)
                            transmissionMetrics.LoadMillicurrent = this.ToLong(kvp.Value);
                            break;
                        case "H20": // Yield today (0.01 Kwh)
                            metrics.Write($"{this.configuration.MetricPrefix}_today", new Dictionary<string, object> { { "yield", this.ToLong(kvp.Value) * 10 } });
                            break;
                        case "H21": // Maximum power today (W)
                            metrics.Write($"{this.configuration.MetricPrefix}_today", new Dictionary<string, object> { { "power", this.ToLong(kvp.Value) } });
                            break;
                        case "CS": // State of operation
                            metrics.Write($"{this.configuration.MetricPrefix}_VICTRON_CS", new Dictionary<string, object> { { "Status", this.ToLong(kvp.Value) } });
                            break;
                        case "ERR": // Error state
                            metrics.Write($"{this.configuration.MetricPrefix}_VICTRON_ERR", new Dictionary<string, object> { { "Status", this.ToLong(kvp.Value) } });
                            break;
                        case "MPPT": // Tracker operation mode
                            metrics.Write($"{this.configuration.MetricPrefix}_VICTRON_MPPT", new Dictionary<string, object> { { "Status", this.ToLong(kvp.Value) } });
                            break;
                        case "LOAD": // Load output State (ON/OFF)
                            metrics.Write($"{this.configuration.MetricPrefix}_load", new Dictionary<string, object> { { "Status", kvp.Value == "ON" ? 1 : 0 } });
                            break;
                        default:
                            break;
                    }
                }

                transmissionMetrics.CalculateMissingData();

                metrics.Write($"{this.configuration.MetricPrefix}_battery", new Dictionary<string, object> { { "voltage", transmissionMetrics.BatteryMillivolt } });
                metrics.Write($"{this.configuration.MetricPrefix}_battery", new Dictionary<string, object> { { "current", transmissionMetrics.BatteryMillicurrent } });
                metrics.Write($"{this.configuration.MetricPrefix}_battery", new Dictionary<string, object> { { "power", transmissionMetrics.BatteryPower } });

                metrics.Write($"{this.configuration.MetricPrefix}_panel", new Dictionary<string, object> { { "voltage", transmissionMetrics.PanelMillivolt } });
                metrics.Write($"{this.configuration.MetricPrefix}_panel", new Dictionary<string, object> { { "current", transmissionMetrics.PanelMillicurrent } });
                metrics.Write($"{this.configuration.MetricPrefix}_panel", new Dictionary<string, object> { { "power", transmissionMetrics.PanelPower } });

                metrics.Write($"{this.configuration.MetricPrefix}_load", new Dictionary<string, object> { { "current", transmissionMetrics.LoadMillicurrent } });
                metrics.Write($"{this.configuration.MetricPrefix}_load", new Dictionary<string, object> { { "power", transmissionMetrics.LoadPower } });
            }
        }

        private long ToLong(string value)
        {
            return value != null ? long.Parse(value) : 0;
        }

        private MetricsCollector CreateMetricsCollector()
        {
            return new CollectorConfiguration()
                .Tag.With("host", Environment.MachineName)
                .Batch.AtInterval(TimeSpan.FromSeconds(this.configuration.IntervalSeconds))
                .WriteTo.InfluxDB(this.configuration.InfluxDbUri, this.configuration.InfluxDbName)
                .CreateCollector();
        }
    }
}
