# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param(
    [switch]$Watch,
    [ValidateRange(1,86400)][int]$IntervalSeconds=10,
    [ValidateRange(0,100000)][int]$Count=0,
    [ValidateRange(100,10000)][int]$TimeoutMs=1000,
    [string]$LogPath,
    [switch]$List
)
$ErrorActionPreference='Stop'
if (-not ('BlackSharkHid' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'BlackSharkHid.cs') }
if ($List) { [BlackSharkHid]::Enumerate() | ConvertTo-Json -Depth 5; return }
if (-not $LogPath) { $LogPath=Join-Path $PSScriptRoot ('logs\session-{0}-{1}.jsonl' -f (Get-Date -Format 'yyyyMMdd-HHmmss'),$PID) }
$LogPath=$ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($LogPath)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($LogPath)) | Out-Null
$mutex=[Threading.Mutex]::new($false,'Local\BlackSharkBatteryDiagnostic-1532-0555')
$locked=$false
try {
    try { $locked=$mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $locked=$true }
    if (-not $locked) { throw 'Another diagnostic instance is already running.' }
    Write-Host "Raw reports: $LogPath"
    $n=0
    do {
        $tick=[Diagnostics.Stopwatch]::StartNew()
        $sample=[BlackSharkHid]::Sample($TimeoutMs)
        $processes=@(Get-Process | Where-Object ProcessName -Match 'Razer|Synapse' | Select-Object ProcessName,Id)
        $record=[ordered]@{ timestamp=$sample.Timestamp; status=$sample.Status; receiverPresent=$sample.ReceiverPresent; connected=$sample.Connected; battery=$sample.Battery; charging=$sample.Charging; chargingRaw=$sample.ChargingRaw; batteryStatus=$sample.BatteryStatus; chargingStatus=$sample.ChargingStatus; error=$sample.Error; razerProcesses=$processes; device=$sample.Device; reports=$sample.Reports }
        [IO.File]::AppendAllText($LogPath,($record | ConvertTo-Json -Depth 8 -Compress)+[Environment]::NewLine)
        $record.Remove('reports'); $record.Remove('device')
        $record | ConvertTo-Json -Depth 5 -Compress
        $n++
        if (-not $Watch -or ($Count -gt 0 -and $n -ge $Count)) { break }
        $delay=[Math]::Max(0,$IntervalSeconds*1000-$tick.ElapsedMilliseconds)
        if($delay -gt 0) { Start-Sleep -Milliseconds $delay }
    } while ($true)
} finally {
    if($locked) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
