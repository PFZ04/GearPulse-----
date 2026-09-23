# SPDX-License-Identifier: GPL-2.0-or-later
$ErrorActionPreference='Stop'
if (-not ('BlackSharkHid' -as [type])) { Add-Type -Path "$PSScriptRoot\BlackSharkHid.cs" }
$checks=0
function Assert($condition,$message) { if (-not $condition) {throw $message}; $script:checks++ }
# Replay a captured report with timing/device fields redacted, including
# unrelated input events. This fixture can be published without local HID paths.
$records=Get-Content (Join-Path $PSScriptRoot 'tests\fixtures\blackshark-replies.jsonl') | ForEach-Object {$_ | ConvertFrom-Json}
foreach($record in $records) {
    foreach($report in $record.reports | Where-Object Direction -eq 'in') {
        [byte[]]$bytes=$report.Hex.Split('-') | ForEach-Object {[Convert]::ToByte($_,16)}
        if($report.Note -eq 'accepted') {
            $value=[BlackSharkHid]::Parse($bytes,$bytes[12])
            Assert ($null -ne $value) 'Captured response rejected'
            if($bytes[12] -eq 0x21) {Assert ($value -eq $record.battery) 'Battery replay mismatch'}
        } else {
            Assert ($null -eq [BlackSharkHid]::Parse($bytes,0x21)) 'Unsolicited event accepted as battery'
        }
    }
}
[byte[]]$valid=New-Object byte[] 64
$valid[0]=2; $valid[12]=0x21; $valid[13]=1; $valid[14]=1
foreach($level in @(0,83,100)) { $valid[15]=$level; Assert ([BlackSharkHid]::Parse($valid,0x21) -eq $level) 'Valid battery rejected' }
$valid[15]=101; Assert ($null -eq [BlackSharkHid]::Parse($valid,0x21)) 'Out-of-range battery accepted'
$valid[15]=83
foreach($offset in @(0,12,13,14)) {
    $bad=$valid.Clone(); $bad[$offset]=0
    Assert ($null -eq [BlackSharkHid]::Parse($bad,0x21)) "Malformed byte $offset accepted"
}
Assert ($null -eq [BlackSharkHid]::Parse([byte[]](2,0,0),0x21)) 'Truncated report accepted'
Assert ($null -eq [BlackSharkHid]::Parse($valid,0x2a)) 'Wrong command accepted'
$valid[12]=0x2a
foreach($flag in @(0,1,255)) { $valid[15]=$flag; Assert ([BlackSharkHid]::Parse($valid,0x2a) -eq $flag) 'Charging raw flag not preserved' }
$empty=[BlackSharkHid+Result]::new()
Assert ($null -eq $empty.Battery -and $null -eq $empty.Charging -and $null -eq $empty.Connected) 'Unknown state must remain null'
$blocked=$false
try { [BlackSharkHid]::Frame(0x95) | Out-Null } catch { $blocked=$true }
Assert $blocked 'Audio write was not blocked'
'Passed {0} protocol checks; no HID commands were sent.' -f $checks
