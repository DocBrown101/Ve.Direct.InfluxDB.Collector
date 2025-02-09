#! /bin/sh
dotnet publish ../src/Ve.Direct.InfluxDB.Collector/Ve.Direct.InfluxDB.Collector.csproj \
	   --runtime linux-arm64 \
	   --self-contained false \
	   -c Release \
	   -v minimal \
	   -f net8.0 \
	   -o ./output/linux-arm64 \
	   -p:PublishReadyToRun=false \
	   -p:PublishSingleFile=true \
	   -p:CopyOutputSymbolsToPublishDirectory=false \
	   --nologo
