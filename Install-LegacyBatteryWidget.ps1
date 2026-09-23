# SPDX-License-Identifier: GPL-2.0-or-later
# Rollback path for the PowerShell/WinForms widget.
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'Stop-BatteryWidget.ps1')
$user=[Security.Principal.WindowsIdentity]::GetCurrent().Name
$pwsh=(Get-Command pwsh).Source
$action=New-ScheduledTaskAction -Execute $pwsh -Argument ('-NoProfile -NonInteractive -STA -WindowStyle Hidden -File "{0}\Start-BatteryWidget.ps1"' -f $PSScriptRoot) -WorkingDirectory $PSScriptRoot
$trigger=New-ScheduledTaskTrigger -AtLogOn -User $user
$principal=New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
$settings=New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -ExecutionTimeLimit ([TimeSpan]::Zero) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable
Register-ScheduledTask -TaskName 'GearPulse' -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description 'GearPulse legacy PowerShell widget.' -Force | Out-Null
Start-ScheduledTask -TaskName 'GearPulse'
Get-ScheduledTask -TaskName 'GearPulse' | Select-Object TaskName,State
