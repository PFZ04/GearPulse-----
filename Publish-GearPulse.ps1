# SPDX-License-Identifier: GPL-2.0-or-later
param([string]$OutputPath=(Join-Path $PSScriptRoot 'publish\win-x64'))
$ErrorActionPreference='Stop'
if(-not $env:DOTNET_CLI_HOME){$env:DOTNET_CLI_HOME=Join-Path $PSScriptRoot '.dotnet-home'}
if(-not $env:NUGET_PACKAGES){$env:NUGET_PACKAGES=Join-Path $PSScriptRoot '.nuget-packages'}
$localDotnet=Join-Path $PSScriptRoot '.dotnet\dotnet.exe'
$dotnet=if(Test-Path -LiteralPath $localDotnet){$localDotnet}else{(Get-Command dotnet -ErrorAction Stop).Source}
$sdk=& $dotnet --list-sdks
if(-not ($sdk | Where-Object {$_ -match '^10\.'})){throw '.NET 10 SDK is required to publish GearPulse.'}
$localFeed=Join-Path $PSScriptRoot '.nuget-feed'
if(Test-Path -LiteralPath $localFeed){
    & $dotnet restore (Join-Path $PSScriptRoot 'src\GearPulse\GearPulse.csproj') --source $localFeed `
        -p:RuntimeIdentifier=win-x64 -p:SelfContained=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
} else {
    & $dotnet restore (Join-Path $PSScriptRoot 'src\GearPulse\GearPulse.csproj') `
        -p:RuntimeIdentifier=win-x64 -p:SelfContained=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
}
if($LASTEXITCODE -ne 0){throw 'GearPulse restore failed.'}
& $dotnet publish (Join-Path $PSScriptRoot 'src\GearPulse\GearPulse.csproj') `
    -c Release --no-restore -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false `
    -o $OutputPath
if($LASTEXITCODE -ne 0){throw 'GearPulse publish failed.'}
$pdb=Join-Path $OutputPath 'GearPulse.pdb'
if(Test-Path -LiteralPath $pdb){Remove-Item -LiteralPath $pdb}
Get-Item (Join-Path $OutputPath 'GearPulse.exe') | Select-Object FullName,Length
