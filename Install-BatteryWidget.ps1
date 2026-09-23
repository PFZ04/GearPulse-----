# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param([string]$ExePath=(Join-Path $PSScriptRoot 'publish\win-x64\GearPulse.exe'))
$ErrorActionPreference='Stop'
$taskName='GearPulse'
$resolvedExe=(Resolve-Path -LiteralPath $ExePath -ErrorAction Stop).Path
if([IO.Path]::GetExtension($resolvedExe) -ine '.exe'){throw 'Expected a published GearPulse.exe.'}

$legacyWallpaper=Get-ScheduledTask -TaskName 'BlackShark Wallpaper Battery' -ErrorAction SilentlyContinue
if($legacyWallpaper){throw 'Legacy Wallpaper Engine bridge found. Restore it from its local backup before installing GearPulse.'}

$existing=Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
if($existing){
    $stateDir=Join-Path $PSScriptRoot 'state'
    [IO.Directory]::CreateDirectory($stateDir) | Out-Null
    $backup=Join-Path $stateDir 'gear-pulse-task-before-wpf.xml'
    if(-not (Test-Path -LiteralPath $backup)){
        Export-ScheduledTask -TaskName $taskName | Set-Content -LiteralPath $backup -Encoding utf8
    }
}
& (Join-Path $PSScriptRoot 'Stop-BatteryWidget.ps1')

# Older installations used this name. Keep that task for rollback, disabled.
$older=Get-ScheduledTask -TaskName 'BlackShark Battery Widget' -ErrorAction SilentlyContinue
if($older){Disable-ScheduledTask -TaskName 'BlackShark Battery Widget' | Out-Null}

$user=[Security.Principal.WindowsIdentity]::GetCurrent().Name
$action=New-ScheduledTaskAction -Execute $resolvedExe -WorkingDirectory (Split-Path -Parent $resolvedExe)
$trigger=New-ScheduledTaskTrigger -AtLogOn -User $user
$principal=New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
$settings=New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -ExecutionTimeLimit ([TimeSpan]::Zero) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description 'GearPulse | 外设脉动 - WPF desktop widget.' -Force | Out-Null
Start-ScheduledTask -TaskName $taskName
Get-ScheduledTask -TaskName $taskName | Select-Object TaskName,State
