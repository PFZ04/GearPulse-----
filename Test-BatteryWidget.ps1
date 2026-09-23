# SPDX-License-Identifier: GPL-2.0-or-later
$ErrorActionPreference='Stop'
$references=@(
    'System.Runtime','System.Private.CoreLib','System.Private.Windows.Core',
    'System.Collections','System.Linq','System.Reflection',
    'System.Threading','System.Threading.Thread','System.Threading.Tasks',
    'System.Windows.Forms','System.Drawing.Common','System.Drawing.Primitives',
    'System.ComponentModel.Primitives','System.Runtime.InteropServices','System.IO.FileSystem'
)
Add-Type -Path (Join-Path $PSScriptRoot 'BatteryWidget.cs') -ReferencedAssemblies $references
$cases=@(
    @{state=[DeviceBatteryState]@{Id='x';Name='X';ReceiverPresent=$true;Battery=85;Charging=$false};text='85% · 未充电'},
    @{state=[DeviceBatteryState]@{Id='x';Name='X';ReceiverPresent=$true;Battery=85;Charging=$true};text='85% · 充电中'},
    @{state=[DeviceBatteryState]@{Id='x';Name='X';ReceiverPresent=$true;Battery=20;Charging=$false};text='20% · 未充电'},
    @{state=[DeviceBatteryState]@{Id='x';Name='X';ReceiverPresent=$true;Battery=0;Charging=$false};text='0% · 未充电'},
    @{state=[DeviceBatteryState]@{Id='x';Name='X';ReceiverPresent=$true;Battery=$null;Charging=$false};text='电量暂不可用'},
    @{state=[DeviceBatteryState]@{Id='x';Name='X';ReceiverPresent=$false;Battery=$null;Charging=$null};text='接收器未连接'},
    @{state=[DeviceBatteryState]@{Id='mouse';Name='ATK';ReceiverPresent=$true;Battery=$null;Status='mouse_offline'};text='鼠标未连接或休眠'},
    @{state=[DeviceBatteryState]@{Id='x';Name='X';ReceiverPresent=$true;Battery=82;Charging=$null};text='82% · 充电状态未知'}
)
foreach($case in $cases) {
    $actual=[BatteryWidgetLayout]::StatusText($case.state)
    if($actual -cne $case.text){throw "Expected '$($case.text)', got '$actual'."}
}
if([BatteryWidgetLayout]::Height(2) -ne [BatteryWidgetLayout]::Height(1)+[BatteryWidgetLayout]::RowHeight) {
    throw 'Second device row did not increase card height.'
}
$states=[DeviceBatteryState[]]@(
    [DeviceBatteryState]@{Id='headset';Name='BLACKSHARK V2 PRO';ReceiverPresent=$true;Battery=20;Charging=$true;Icon='headset'},
    [DeviceBatteryState]@{Id='second';Name='FUTURE DEVICE';ReceiverPresent=$true;Battery=70;Charging=$false;Icon='battery'}
)
$providers=[IBatteryProvider[]]@($states | ForEach-Object {[FixedBatteryProvider]::new($_)})
$window=[BatteryWidgetWindow]::new($providers,(Join-Path $PSScriptRoot 'state\widget.stop'))
try {
    $bitmap=[Drawing.Bitmap]::new($window.Width,$window.Height)
    try {
        $window.DrawToBitmap($bitmap,[Drawing.Rectangle]::new(0,0,$bitmap.Width,$bitmap.Height))
        [IO.Directory]::CreateDirectory((Join-Path $PSScriptRoot 'state')) | Out-Null
        $bitmap.Save((Join-Path $PSScriptRoot 'state\widget-preview-two-devices.png'))
    } finally {$bitmap.Dispose()}
} finally {$window.Dispose()}
$hidden=[IBatteryProvider[]]@(
    [FixedBatteryProvider]::new($states[0]),
    [FixedBatteryProvider]::new([DeviceBatteryState]@{Id='disconnected';Status='hidden'})
)
$compact=[BatteryWidgetWindow]::new($hidden,(Join-Path $PSScriptRoot 'state\widget.stop'))
try {
    if($compact.Height -ne [BatteryWidgetLayout]::Height(1)) {
        throw 'Absent mouse still occupies a row.'
    }
} finally {$compact.Dispose()}
$parseErrors=@()
Get-ChildItem "$PSScriptRoot\*.ps1" | ForEach-Object {
    $tokens=$null; $errors=$null
    $null=[Management.Automation.Language.Parser]::ParseFile($_.FullName,[ref]$tokens,[ref]$errors)
    $parseErrors+=@($errors)
}
if($parseErrors.Count){throw ($parseErrors | Out-String)}
'Passed 8 status cases, hidden-row layout, two-device render, and PowerShell syntax checks.'
