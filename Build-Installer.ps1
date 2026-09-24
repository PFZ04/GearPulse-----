# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param(
    [string]$IsccPath=(Join-Path $PSScriptRoot 'publish\build-tools\InnoSetup7\ISCC.exe'),
    [switch]$TestBuild
)
$ErrorActionPreference='Stop'
$exe=Join-Path $PSScriptRoot 'publish\win-x64\GearPulse.exe'
$file=Get-Item -LiteralPath $exe -ErrorAction Stop
if($file.VersionInfo.FileVersion -ne '1.2.0.0'){throw "Expected GearPulse.exe file version 1.2.0.0; found $($file.VersionInfo.FileVersion)."}
if(-not (Test-Path -LiteralPath $IsccPath -PathType Leaf)){throw "Inno Setup 7 compiler not found: $IsccPath"}
$script=Join-Path $PSScriptRoot 'installer\GearPulse.iss'
if($TestBuild){
    $testRoot=Join-Path $PSScriptRoot 'publish\installer-test'
    $testInstall=Join-Path $testRoot 'app'
    $testData=Join-Path $testRoot 'data'
    & $IsccPath '--define=TestBuild=1' "--define=TestInstallDir=$testInstall" "--define=TestUserDataDir=$testData" $script
}
else {& $IsccPath $script}
if($LASTEXITCODE -ne 0){throw "Inno Setup compilation failed ($LASTEXITCODE)."}
$name=if($TestBuild){'GearPulse-1.2.0-Test-Setup.exe'}else{'GearPulse-1.2.0-Setup.exe'}
$output=Join-Path $PSScriptRoot "publish\$name"
$result=Get-Item -LiteralPath $output -ErrorAction Stop
$hash=Get-FileHash -LiteralPath $output -Algorithm SHA256
[pscustomobject]@{Path=$result.FullName;Bytes=$result.Length;FileVersion=$result.VersionInfo.FileVersion;SHA256=$hash.Hash}
