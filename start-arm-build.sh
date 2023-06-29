#! /bin/sh
dotnet publish ./src/Ve.Direct.InfluxDB.Collector/Ve.Direct.InfluxDB.Collector.csproj \
	   --runtime linux-arm \
	   --self-contained false \
	   -c Release \
	   -v minimal \
	   -f net7.0 \
	   -o ./arm-build \
	   -p:PublishReadyToRun=false \
	   -p:PublishSingleFile=true \
	   -p:CopyOutputSymbolsToPublishDirectory=false \
	   --nologo
