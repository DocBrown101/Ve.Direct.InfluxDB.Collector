# Ve.Direct InfluxDB Collector
Collects data from your Victron SmartSolar MPPT and sends it to InfluxDB

[![Maintainability Rating](https://sonarcloud.io/api/project_badges/measure?project=DocBrown101_Ve.Direct.InfluxDB.Collector&metric=sqale_rating)](https://sonarcloud.io/dashboard?id=DocBrown101_Ve.Direct.InfluxDB.Collector)
[![Lines of Code](https://sonarcloud.io/api/project_badges/measure?project=DocBrown101_Ve.Direct.InfluxDB.Collector&metric=ncloc)](https://sonarcloud.io/dashboard?id=DocBrown101_Ve.Direct.InfluxDB.Collector)
[![Security Rating](https://sonarcloud.io/api/project_badges/measure?project=DocBrown101_Ve.Direct.InfluxDB.Collector&metric=security_rating)](https://sonarcloud.io/dashboard?id=DocBrown101_Ve.Direct.InfluxDB.Collector)

![Preview](https://github.com/DocBrown101/Ve.Direct.InfluxDB.Collector/blob/main/docs/image.jpg)

## Requirements
* [The matching Grafana Dashboard](https://grafana.com/grafana/dashboards/14597)
* [Solar charge controller](https://www.victronenergy.com/solar-charge-controllers)
* RaspberryPI with .NET or MONO or any other PC that runs 24/7 and has a serial port
* Ve.Direct cable to the RaspberryPI or PC

## Example Usage
```
/path/to/Ve.Direct.InfluxDB.Collector.exe -o Influx
```

You can alternatively run this as a crontab.
```
@reboot /usr/bin/mono -- /path/to/Ve.Direct.InfluxDB.Collector.exe -o Influx >> /var/log/cron.log 2>&1
```

Inspired by https://github.com/oyebayo/vedirect
