$BuildPathWinX64 = ".\output\win-x64"
$BuildPathLinuxX64 = ".\output\linux-x64"
$BuildPathLinuxARM64 = ".\output\linux-arm64"
$Version = Get-Date -Format "yyyy-MM-dd" # 2020-11-1
$VersionDot = $Version -replace '-','.'

# Dotnet restore and build
dotnet publish "$PSScriptRoot\..\src\Ve.Direct.InfluxDB.Collector\Ve.Direct.InfluxDB.Collector.csproj" `
	   --runtime win-x64 `
	   --self-contained false `
	   -c Release `
	   -v minimal `
	   -o $BuildPathWinX64 `
	   -f net9.0 `
	   -p:PublishReadyToRun=false `
	   -p:PublishSingleFile=true `
	   -p:CopyOutputSymbolsToPublishDirectory=false `
	   -p:Version=$VersionDot `
	   --nologo

dotnet publish "$PSScriptRoot\..\src\Ve.Direct.InfluxDB.Collector\Ve.Direct.InfluxDB.Collector.csproj" `
	   --runtime linux-x64 `
	   --self-contained false `
	   -c Release `
	   -v minimal `
	   -o $BuildPathLinuxX64 `
	   -f net9.0 `
	   -p:PublishReadyToRun=false `
	   -p:PublishSingleFile=true `
	   -p:CopyOutputSymbolsToPublishDirectory=false `
	   -p:Version=$VersionDot `
	   --nologo

dotnet publish "$PSScriptRoot\..\src\Ve.Direct.InfluxDB.Collector\Ve.Direct.InfluxDB.Collector.csproj" `
	   --runtime linux-arm64 `
	   --self-contained false `
	   -c Release `
	   -v minimal `
	   -o $BuildPathLinuxARM64 `
	   -f net9.0 `
	   -p:PublishReadyToRun=false `
	   -p:PublishSingleFile=true `
	   -p:CopyOutputSymbolsToPublishDirectory=false `
	   -p:Version=$VersionDot `
	   --nologo

Compress-Archive -Path "$BuildPathWinX64" -DestinationPath "$BuildPathWinX64"
Compress-Archive -Path "$BuildPathLinuxX64" -DestinationPath "$BuildPathLinuxX64"
Compress-Archive -Path "$BuildPathLinuxARM64" -DestinationPath "$BuildPathLinuxARM64"