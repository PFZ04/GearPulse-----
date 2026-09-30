# Bluetooth headset battery fix — 2026-09-30

- Before: the active `OPPO Enco Air5s（星光版）` render endpoint was identified as a Bluetooth headset, but its battery was unknown. `AudioHeadsetDiscovery.Resolve` only copied Windows battery properties for speakers.
- After: the same active headset resolved to 70%, matching the Windows Bluetooth Hands-Free AG node in its physical container. No HID commands or driver changes were required.
- Bluetooth headsets now share the speaker path for battery validation and explicit disconnection filtering. A missing or invalid percentage remains unknown on the next sample.
- All 227 offline smoke checks passed, including headset battery propagation, mismatched endpoint containers, ambiguous identity, disconnected sources, missing/invalid values, and valid 0%.
- Release build and installer succeeded: `publish/bluetooth-headset-fix/GearPulse.exe` and `publish/GearPulse-1.3.10-Setup.exe`.
- After user approval, the 1.3.10 installer completed successfully. The running process, enabled login task, and Start menu shortcut all point to `%LOCALAPPDATA%/Programs/GearPulse/GearPulse.exe`. Installed EXE and README SHA-256 hashes match the release files; the shortcut uses the application's embedded icon.
- A subsequent read-only audio diagnostic still reported the OPPO headset at 70%. Physical headset power-off/reconnect and the installed card display remain unverified; disconnect and stale-value behavior were checked offline.
- Existing unrelated `MainWindow.xaml.cs` changes were preserved and are included in the candidate build.
