# SPDX-License-Identifier: GPL-2.0-or-later
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'Stop-BatteryWidget.ps1')
$task=Get-ScheduledTask -TaskName 'GearPulse' -ErrorAction SilentlyContinue
if($task){Unregister-ScheduledTask -TaskName 'GearPulse' -Confirm:$false}
'GearPulse autostart removed.'
