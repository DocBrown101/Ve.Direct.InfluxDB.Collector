$Version = Get-Date -Format "yyyy-MM-dd" # 2020-11-1
$VersionDot = $Version -replace '-','.'

# Dotnet restore and build
dotnet publish "$PSScriptRoot\src\Ve.Direct.InfluxDB.Collector\Ve.Direct.InfluxDB.Collector.csproj" `
	   --runtime win-x64 `
	   --self-contained false `
	   -c Release `
	   -v minimal `
	   -o ./build `
	   -f net8.0 `
	   -p:PublishReadyToRun=false `
	   -p:PublishSingleFile=true `
	   -p:CopyOutputSymbolsToPublishDirectory=false `
	   -p:Version=$VersionDot `
	   --nologo
