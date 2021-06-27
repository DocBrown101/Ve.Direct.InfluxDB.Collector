using System;
using System.Collections.Generic;
using System.Threading.Tasks;

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
    public class MetricsCompositor
    {
        private readonly MetricsConfigurationModel configuration;
        private readonly PayloadClient payloadClient;

        private DateTime lastWriteTime = DateTime.MinValue;

        public MetricsCompositor(MetricsConfigurationModel configuration)
        {
            this.configuration = configuration;
            this.payloadClient = new PayloadClient(configuration);
        }

        public async Task SendMetricsCallback(Dictionary<string, string> rawData)
        {
            Logger.Debug("Just received new raw data!");

            this.payloadClient.AddPayload(this.ConvertToMetricsTransmissionModel(rawData));

            if ((DateTime.Now - this.lastWriteTime).TotalSeconds >= this.configuration.IntervalSeconds)
            {
                this.lastWriteTime = DateTime.Now;

                await this.payloadClient.TrySendPayload().ConfigureAwait(false);
            }
        }

        private MetricsTransmissionModel ConvertToMetricsTransmissionModel(Dictionary<string, string> data)
        {
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
                        transmissionMetrics.TodayYield = this.ToLong(kvp.Value) * 10;
                        break;
                    case "H21": // Maximum power today (W)
                        transmissionMetrics.TodayPower = this.ToLong(kvp.Value);
                        break;
                    case "CS": // State of operation
                        transmissionMetrics.VICTRON_CS_Status = this.ToInt(kvp.Value);
                        break;
                    case "ERR": // Error state
                        transmissionMetrics.VICTRON_ERR_Status = this.ToInt(kvp.Value);
                        break;
                    case "MPPT": // Tracker operation mode
                        transmissionMetrics.VICTRON_MPPT_Status = this.ToInt(kvp.Value);
                        break;
                    case "LOAD": // Load output State (ON/OFF)
                        transmissionMetrics.LoadStatus = kvp.Value == "ON" ? 1 : 0;
                        break;
                    default:
                        break;
                }
            }

            transmissionMetrics.CalculateMissingData();

            return transmissionMetrics;
        }

        private long ToLong(string value)
        {
            return value != null ? long.Parse(value) : 0;
        }

        private int ToInt(string value)
        {
            return value != null ? int.Parse(value) : 0;
        }
    }
}
