using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using InfluxDB.LineProtocol.Client;
using InfluxDB.LineProtocol.Payload;

namespace Ve.Direct.InfluxDB.Collector.Metrics
{
    public class LineProtocolPointTest
    {
        private readonly LineProtocolClient client;

        public LineProtocolPointTest()
        {
            this.client = new LineProtocolClient(new Uri("http://192.168.0.220:8086"), "solar");
        }

        public async Task Test2()
        {
            var data = new LineProtocolPoint("working_set",
                            new Dictionary<string, object> { { "value", 12 }, },
                            new Dictionary<string, string> { { "host", Environment.MachineName } }, DateTime.UtcNow);

            var payload = new LineProtocolPayload();
            payload.Add(data);

            var influxResult = await this.client.WriteAsync(payload);
            if (!influxResult.Success)
            {
                Console.Error.WriteLine(influxResult.ErrorMessage);
            }


            //metrics.Write("test_influxDbTest", new Dictionary<string, object> { { "test", "1" } });

            Console.WriteLine("InfluxDb write operation completed successfully");
        }
    }
}
