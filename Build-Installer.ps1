# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param(
    [string]$IsccPath=(Join-Path $PSScriptRoot 'publish\build-tools\InnoSetup7\ISCC.exe'),
    [string]$SourceExe=(Join-Path $PSScriptRoot 'publish\win-x64\GearPulse.exe'),
    [string]$ExpectedVersion='1.2.0',
    [switch]$TestBuild
)
$ErrorActionPreference='Stop'
$file=Get-Item -LiteralPath $SourceExe -ErrorAction Stop
if($ExpectedVersion -notmatch '^\d+\.\d+\.\d+$'){throw "ExpectedVersion must be major.minor.patch: $ExpectedVersion"}
if($file.VersionInfo.FileVersion -ne "$ExpectedVersion.0"){
    throw "Expected GearPulse.exe file version $ExpectedVersion.0; found $($file.VersionInfo.FileVersion)."
}
if(-not (Test-Path -LiteralPath $IsccPath -PathType Leaf)){throw "Inno Setup 7 compiler not found: $IsccPath"}
$script=Join-Path $PSScriptRoot 'installer\GearPulse.iss'
$name=if($TestBuild){"GearPulse-$ExpectedVersion-Test-Setup.exe"}else{"GearPulse-$ExpectedVersion-Setup.exe"}
$defines=@(
    "--define=AppVersion=$ExpectedVersion",
    "--define=SourceExe=$($file.FullName)",
    "--define=SetupName=GearPulse-$ExpectedVersion-Setup",
    "--define=TestSetupName=GearPulse-$ExpectedVersion-Test-Setup"
)
if($TestBuild){
    $testRoot=Join-Path $PSScriptRoot 'publish\installer-test'
    $testInstall=Join-Path $testRoot 'app'
    $testData=Join-Path $testRoot 'data'
    & $IsccPath @defines '--define=TestBuild=1' "--define=TestInstallDir=$testInstall" "--define=TestUserDataDir=$testData" $script
}
else {& $IsccPath @defines $script}
if($LASTEXITCODE -ne 0){throw "Inno Setup compilation failed ($LASTEXITCODE)."}
$output=Join-Path $PSScriptRoot "publish\$name"
$result=Get-Item -LiteralPath $output -ErrorAction Stop
$hash=Get-FileHash -LiteralPath $output -Algorithm SHA256
[pscustomobject]@{Path=$result.FullName;Bytes=$result.Length;FileVersion=$result.VersionInfo.FileVersion;SHA256=$hash.Hash}
