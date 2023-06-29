#! /bin/sh
dotnet publish ./src/Ve.Direct.InfluxDB.Collector/Ve.Direct.InfluxDB.Collector.csproj \
	   --runtime linux-x64 \
	   --self-contained false \
	   -c Release \
	   -v minimal \
	   -f net7.0 \
	   -o ./x64-build \
	   -p:PublishReadyToRun=false \
	   -p:PublishSingleFile=true \
	   -p:CopyOutputSymbolsToPublishDirectory=false \
	   --nologo
