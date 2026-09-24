# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Preflight','Install','Uninstall')][string]$Mode,
    [Parameter(Mandatory)][string]$TaskName,
    [Parameter(Mandatory)][string]$ExePath,
    [Parameter(Mandatory)][string]$ErrorFile,
    [switch]$DisableAutostart
)
$ErrorActionPreference='Stop'

function Get-OwnedTask {
    $task=Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    if(-not $task){return $null}
    $action=@($task.Actions)[0]
    $command=[string]$action.Execute
    $arguments=[string]$action.Arguments
    $isWpf=[IO.Path]::GetFileName($command) -ieq 'GearPulse.exe'
    $isLegacy=([IO.Path]::GetFileName($command) -in @('pwsh.exe','powershell.exe')) -and
        $arguments -match '(?i)(?:^|[\\/"\s])Start-BatteryWidget\.ps1(?:["\s]|$)'
    if(-not ($isWpf -or $isLegacy)){
        throw "Task '$TaskName' belongs to another program ($command). Installation will not replace it."
    }
    return $task
}

function Stop-Widget {
    if($TaskName -ne 'GearPulse'){return}
    try {
        $signal=[Threading.EventWaitHandle]::OpenExisting('Local\GearPulseWpfExit')
        try {$signal.Set() | Out-Null} finally {$signal.Dispose()}
    } catch [Threading.WaitHandleCannotBeOpenedException] { }
    $mutex=[Threading.Mutex]::new($false,'Local\GearPulseWpf')
    try {
        $held=$false
        try {$held=$mutex.WaitOne(15000)} catch [Threading.AbandonedMutexException] {$held=$true}
        if(-not $held){throw 'GearPulse did not stop within 15 seconds.'}
        $mutex.ReleaseMutex()
    } finally {$mutex.Dispose()}
}

try {
    $task=if($Mode -eq 'Uninstall'){
        Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    }else{Get-OwnedTask}
    switch($Mode){
        'Preflight' {
            Stop-Widget
            if($task -and $task.State -eq 'Running'){
                Stop-ScheduledTask -TaskName $TaskName -ErrorAction Stop
            }
        }
        'Install' {
            $fullPath=[IO.Path]::GetFullPath($ExePath)
            if(-not (Test-Path -LiteralPath $fullPath -PathType Leaf)){throw "Missing application: $fullPath"}
            $user=[Security.Principal.WindowsIdentity]::GetCurrent().Name
            $action=New-ScheduledTaskAction -Execute $fullPath -WorkingDirectory ([IO.Path]::GetDirectoryName($fullPath))
            $trigger=New-ScheduledTaskTrigger -AtLogOn -User $user
            $principal=New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
            $settings=New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -ExecutionTimeLimit ([TimeSpan]::Zero) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable
            Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description 'GearPulse desktop battery widget' -Force | Out-Null
            if($DisableAutostart){Disable-ScheduledTask -TaskName $TaskName | Out-Null}
        }
        'Uninstall' {
            Stop-Widget
            if($task){
                $actual=[IO.Path]::GetFullPath([string]@($task.Actions)[0].Execute)
                $expected=[IO.Path]::GetFullPath($ExePath)
                if([string]::Equals($actual,$expected,[StringComparison]::OrdinalIgnoreCase)){
                    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
                }
            }
        }
    }
    exit 0
} catch {
    $message=$_.Exception.Message
    try {Set-Content -LiteralPath $ErrorFile -Value $message -Encoding utf8} catch { }
    [Console]::Error.WriteLine($message)
    exit 1
}
