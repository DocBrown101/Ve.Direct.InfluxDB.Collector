$Runtimes = "win-x64", "linux-x64", "linux-arm64"
$VersionDot = Get-Date -Format "yyyy.MM.dd"

# Dotnet restore and build
foreach ($Runtime in $Runtimes) {
	dotnet publish "$PSScriptRoot\..\src\Ve.Direct.InfluxDB.Collector\Ve.Direct.InfluxDB.Collector.csproj" `
		--runtime $Runtime `
		--self-contained false `
		-c Release `
		-v minimal `
		-o ".\output\$Runtime" `
		-f net10.0 `
		-p:PublishReadyToRun=false `
		-p:PublishSingleFile=true `
		-p:CopyOutputSymbolsToPublishDirectory=false `
		-p:Version=$VersionDot `
		--nologo
}

foreach ($Runtime in $Runtimes) {
	Compress-Archive -Path ".\output\$Runtime" -DestinationPath ".\output\$Runtime"
}
