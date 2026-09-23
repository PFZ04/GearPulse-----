# SPDX-License-Identifier: GPL-2.0-or-later
$ErrorActionPreference='Stop'
$exe=Join-Path $PSScriptRoot 'publish\win-x64\GearPulse.exe'
if(Test-Path -LiteralPath $exe){
    & $exe --exit
    $instance=[Threading.Mutex]::new($false,'Local\GearPulseWpf')
    try {
        $held=$false
        try {$held=$instance.WaitOne(15000)} catch [Threading.AbandonedMutexException] {$held=$true}
        if(-not $held){throw 'WPF widget did not stop within 15 seconds.'}
        $instance.ReleaseMutex()
    } finally {$instance.Dispose()}
}

# The legacy launcher uses a separate mutex and stop file.
$stateDir=Join-Path $PSScriptRoot 'state'
[IO.Directory]::CreateDirectory($stateDir) | Out-Null
$stopFile=Join-Path $stateDir 'widget.stop'
Set-Content -LiteralPath $stopFile -Value 'stop' -Encoding utf8
$legacy=[Threading.Mutex]::new($false,'Local\GearPulseWidget')
try {
    $held=$false
    try {$held=$legacy.WaitOne(15000)} catch [Threading.AbandonedMutexException] {$held=$true}
    if(-not $held){throw 'Legacy widget did not stop within 15 seconds.'}
    $legacy.ReleaseMutex()
} finally {$legacy.Dispose()}
'GearPulse stopped.'
