# Ve.Direct.InfluxDB.Collector

Ve.Direct.InfluxDB.Collector reads the Victron VE.Direct text protocol and writes metrics to InfluxDB 2.x or an Influx-compatible VictoriaMetrics endpoint. One process can monitor multiple VE.Direct devices.

<p align="center">
  <a href="https://sonarcloud.io/summary/new_code?id=DocBrown101_Ve.Direct.InfluxDB.Collector">
    <img src="https://sonarcloud.io/api/project_badges/quality_gate?project=DocBrown101_Ve.Direct.InfluxDB.Collector" />
  </a>
</p>

<p align="center">
  <a href="https://sonarcloud.io/dashboard?id=DocBrown101_Ve.Direct.InfluxDB.Collector"><img src="https://sonarcloud.io/api/project_badges/measure?project=DocBrown101_Ve.Direct.InfluxDB.Collector&metric=sqale_rating" /></a>
  <a href="https://sonarcloud.io/dashboard?id=DocBrown101_Ve.Direct.InfluxDB.Collector"><img src="https://sonarcloud.io/api/project_badges/measure?project=DocBrown101_Ve.Direct.InfluxDB.Collector&metric=security_rating" /></a>
  <a href="https://sonarcloud.io/summary/new_code?id=DocBrown101_Ve.Direct.InfluxDB.Collector"><img src="https://sonarcloud.io/api/project_badges/measure?project=DocBrown101_Ve.Direct.InfluxDB.Collector&metric=vulnerabilities" /></a>
</p>

![Preview](https://github.com/DocBrown101/Ve.Direct.InfluxDB.Collector/blob/main/docs/image1.jpg)

![Preview](https://github.com/DocBrown101/Ve.Direct.InfluxDB.Collector/blob/main/docs/image2.jpg)

## Requirements

- [Grafana](https://hub.docker.com/r/grafana/grafana) and [InfluxDB 2.x](https://hub.docker.com/_/influxdb/tags?page=1&name=2.7) or [VictoriaMetrics](https://hub.docker.com/r/victoriametrics/victoria-metrics)
- [Grafana dashboard](https://grafana.com/grafana/dashboards/14597)
- One or more Victron devices with a VE.Direct text-protocol connection
- A Raspberry Pi or another always-on computer with [.NET 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- Permission to open the relevant serial devices

![Direct connection without a USB adapter](https://github.com/DocBrown101/Ve.Direct.InfluxDB.Collector/blob/main/docs/No-VEDirect-USB.jpg)

![VE.Direct connection to a Raspberry Pi](https://github.com/DocBrown101/Ve.Direct.InfluxDB.Collector/blob/main/docs/Connect-VEDirect-To-RPi.jpg)

## Device Identity

Devices are identified by the VE.Direct `SER#` field. Port names are transport endpoints and may change after a reboot or USB reconnect without changing the time-series identity.

Every point contains these tags:

```text
host=<collector-host>
device=<VE.Direct SER#>
```

VictoriaMetrics converts Influx tags to labels, so `device` has the same meaning in both backends. Payload data is local to each frame, allowing devices to write concurrently without sharing state.

## Auto-Discovery

The collector polls `SerialPort.GetPortNames()` and starts an independent reader and parser for each port. The default scan interval is five seconds and can be changed with `--scan-interval`.

Automatic discovery is the only mode. The former `-p`/`--port` and `--auto-discover` options have been removed. The former `-i`/`--interval` batch option has also been removed because readers write independently instead of using a shared batch.

## Hotplug and Reconnect

- Each port has its own reader and parser state.
- New ports are opened during the next scan.
- Removed ports are stopped without affecting other readers.
- Open and read failures are isolated to the affected port and retried.
- Shutdown stops all readers and disposes the database client.

Frames without a non-empty `SER#` are discarded. A warning is logged once per affected port until a valid serial number is received.

If two ports report the same `SER#`, the first reader remains active and the later source is suppressed. After the first reader disconnects, another port can take over that identity.

## Running the Collector

Run with InfluxDB output:

```bash
/path/to/Ve.Direct.InfluxDB.Collector \
  -o Influx \
  --influxDbUrl http://localhost:8086 \
  --influxDbBucket solar \
  --influxDbOrg home
```

To use a three-second discovery interval, add `--scan-interval 3`.

With two connected devices, startup output resembles:

```text
Monitoring serial port /dev/ttyUSB0.
Monitoring serial port /dev/ttyUSB1.
Detected VE.Direct device HQ123456 on /dev/ttyUSB0.
Detected VE.Direct device HQ987654 on /dev/ttyUSB1.
```

## Grafana Queries

Measurement and field names remain unchanged. For multiple devices, filter or group queries by the `device` tag.

InfluxDB Flux filter:

```flux
from(bucket: "solar")
  |> range(start: -24h)
  |> filter(fn: (r) => r._measurement == "ve_direct_battery")
  |> filter(fn: (r) => r.device == "HQ123456")
```

Flux grouping:

```flux
|> group(columns: ["device"])
```

VictoriaMetrics/PromQL:

```promql
ve_direct_battery_voltage{device="HQ123456"}
sum by (device) (ve_direct_panel_power)
```

## systemd

```ini
[Unit]
Description=VE.Direct metrics collector
After=network.target

[Service]
User=currentUser
Environment=DOTNET_ROOT=/home/currentUser/dotnet
Environment=PATH=/home/currentUser/dotnet
ExecStart=/home/currentUser/Ve.Direct.InfluxDB.Collector --influxDbBucket solar -o Influx
Restart=on-failure
RestartSec=5s

[Install]
WantedBy=multi-user.target
```
