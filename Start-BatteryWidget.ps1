# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$stateDir=Join-Path $PSScriptRoot 'state'
[IO.Directory]::CreateDirectory($stateDir) | Out-Null
$stopFile=Join-Path $stateDir 'widget.stop'
$logFile=Join-Path $stateDir 'widget.log'
$instance=[Threading.Mutex]::new($false,'Local\GearPulseWidget')
$owned=$false
try {
    try {$owned=$instance.WaitOne(0)} catch [Threading.AbandonedMutexException] {$owned=$true}
    if(-not $owned) {return}
    Remove-Item -LiteralPath $stopFile -ErrorAction SilentlyContinue

    Add-Type -Path (Join-Path $PSScriptRoot 'BlackSharkHid.cs')
    Add-Type -Path (Join-Path $PSScriptRoot 'AtkMouseHid.cs')
    # PowerShell bundles Windows Forms. Add-Type needs these references explicitly
    # when compiling a second C# file that uses its desktop UI assemblies.
    $references=@(
        'System.Runtime','System.Private.CoreLib','System.Private.Windows.Core',
        'System.Collections','System.Linq','System.Reflection',
        'System.Threading','System.Threading.Thread','System.Threading.Tasks',
        'System.Windows.Forms','System.Drawing.Common','System.Drawing.Primitives',
        'System.ComponentModel.Primitives','System.Runtime.InteropServices','System.IO.FileSystem'
    )
    Add-Type -Path (Join-Path $PSScriptRoot 'BatteryWidget.cs') -ReferencedAssemblies $references
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.Application]::EnableVisualStyles()
    $providers=[IBatteryProvider[]]@(
        [BlackSharkBatteryProvider]::new(),
        [AtkMouseBatteryProvider]::new(0x1278),
        [AtkMouseBatteryProvider]::new(0x10c9)
    )
    Add-Content -LiteralPath $logFile -Value ("{0} Started." -f [DateTimeOffset]::Now.ToString('o'))
    do {
        $window=[BatteryWidgetWindow]::new($providers,$stopFile)
        try {[System.Windows.Forms.Application]::Run($window)}
        finally {$window.Dispose()}
        if(-not (Test-Path $stopFile)) {Start-Sleep -Seconds 2}
    } while(-not (Test-Path $stopFile))
} catch {
    Add-Content -LiteralPath $logFile -Value ("{0} Error: {1}" -f [DateTimeOffset]::Now.ToString('o'),$_.Exception.ToString())
    throw
} finally {
    if($owned) {
        Add-Content -LiteralPath $logFile -Value ("{0} Stopped." -f [DateTimeOffset]::Now.ToString('o'))
        $instance.ReleaseMutex()
    }
    $instance.Dispose()
}
