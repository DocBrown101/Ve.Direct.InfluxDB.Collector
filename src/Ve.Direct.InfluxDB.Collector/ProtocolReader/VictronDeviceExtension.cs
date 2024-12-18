using System.Collections.Generic;

namespace Ve.Direct.InfluxDB.Collector.ProtocolReader
{
    public static class VictronDeviceExtension
    {
        private static readonly Dictionary<string, string> devices = new()
        {
            { "0x300", "BlueSolar MPPT 70/15" },
            { "0xA040", "BlueSolar MPPT 75/50" },
            { "0xA041", "BlueSolar MPPT 150/35" },
            { "0xA042", "BlueSolar MPPT 75/15" },
            { "0xA043", "BlueSolar MPPT 100/15" },
            { "0xA044", "BlueSolar MPPT 100/30" },
            { "0xA045", "BlueSolar MPPT 100/50" },
            { "0xA046", "BlueSolar MPPT 150/70" },
            { "0xA047", "BlueSolar MPPT 150/100" },
            { "0xA049", "BlueSolar MPPT 100/50 rev2" },
            { "0xA04A", "BlueSolar MPPT 100/30 rev2" },
            { "0xA04B", "BlueSolar MPPT 150/35 rev2" },
            { "0xA04C", "BlueSolar MPPT 75/10" },
            { "0xA04D", "BlueSolar MPPT 150/45" },
            { "0xA04E", "BlueSolar MPPT 150/60" },
            { "0xA04F", "BlueSolar MPPT 150/85" },
            { "0xA050", "SmartSolar MPPT 250/100" },
            { "0xA051", "SmartSolar MPPT 150/100*" },
            { "0xA052", "SmartSolar MPPT 150/85*" },
            { "0xA053", "SmartSolar MPPT 75/15" },
            { "0xA054", "SmartSolar MPPT 75/10" },
            { "0xA055", "SmartSolar MPPT 100/15" },
            { "0xA056", "SmartSolar MPPT 100/30" },
            { "0xA057", "SmartSolar MPPT 100/50" },
            { "0xA058", "SmartSolar MPPT 150/35" },
            { "0xA059", "SmartSolar MPPT 150/100 rev2" },
            { "0xA05A", "SmartSolar MPPT 150/85 rev2" },
            { "0xA05B", "SmartSolar MPPT 250/70" },
            { "0xA05C", "SmartSolar MPPT 250/85" },
            { "0xA05D", "SmartSolar MPPT 250/60" },
            { "0xA05E", "SmartSolar MPPT 250/45" },
            { "0xA05F", "SmartSolar MPPT 100/20" },
            { "0xA060", "SmartSolar MPPT 100/20 48V" },
            { "0xA061", "SmartSolar MPPT 150/45" },
            { "0xA062", "SmartSolar MPPT 150/60" },
            { "0xA063", "SmartSolar MPPT 150/70" },
            { "0xA064", "SmartSolar MPPT 250/85 rev2" },
            { "0xA065", "SmartSolar MPPT 250/100 rev2" },
        };

        public static string GetVictronDeviceNameByPid(this string self)
        {
            return devices.ContainsKey(self) ? $"{self} ({devices[self]})" : $"unknown device pid: {self}";
        }
    }
}
