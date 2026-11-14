[简体中文](privacy-policy.md) | **English**

# GammaLift Privacy Policy

**Effective date: November 13, 2026**

This privacy policy applies to the GammaLift desktop application for Windows ("the app"). It explains what information the app touches, where that information is stored, and what the app does not do.

## Summary

**GammaLift does not connect to the network and does not collect any personal information.** There is no account system, no advertising, no analytics, and no crash reporting. The program contains no network request code, and every feature works offline. Everything you adjust in the app is written to a single JSON file on your own computer.

## Information the app touches

GammaLift is a local tool. It has no servers and nowhere to send data. The following information is produced and used only on your machine.

### Monitor identity and display parameters

- **Monitor device ID**: the device interface path provided by Windows (including vendor and model codes). It serves only as a key in the settings file, so each monitor keeps its own settings and is still recognized after you switch ports or plug it back in.
- **Display parameters**: brightness, contrast, gamma, and saturation values, the choice between software LUT and hardware DDC/CI, and the contents of your presets (profiles).
- **Gamma LUT baseline**: the current LUT read from a monitor the first time the app sees it (usually your system ICC / vcgt calibration curve). It is stored so that "Reset" can restore your original system calibration exactly, instead of writing a linear curve.

### App settings

Preferences such as interface language, UI scale, hotkey bindings and gestures, notification switches, autostart state, and whether the hardware panel is expanded.

Location: by default `GammaLiftSettings.json` in the same folder as `GammaLift.exe`. If that folder is not writable (for example `Program Files` or a read-only share), the file goes to the same name inside your Documents folder. On first launch, if a legacy configuration is found under `%APPDATA%` (GammaLift / Lumin / GammaPanelX), a copy is made; the old folder is left untouched.

### Keyboard input (only to recognize hotkey gestures)

To support single-click, double-click, and long-press global hotkeys, the app observes key presses and releases through a keyboard hook on a background thread. It only compares key codes and timing to decide whether a configured gesture matched:

- Keystrokes are **never** recorded, stored, written to a file, or transmitted;
- Characters and IME content are not parsed, and no per-window or per-program identification is performed;
- The hook is removed as soon as global hotkeys are turned off or the app exits.

Hotkeys are registered only after you set and enable them; the app never listens to the keyboard in order to learn what you type.

### Clipboard (write-only)

The app writes to the Windows clipboard only when you click **Copy** next to the share code, and only that share code text. It does not touch the clipboard otherwise: to import a share code, **you** paste the text into the input field, and the app does not read clipboard contents.

### Registry

Written only when you turn on the corresponding feature, and only in two places:

| Location | Value | When |
|----------|-------|------|
| `GammaLift` value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` | Full path to `GammaLift.exe` | You enable "Start with Windows"; the value is deleted when you turn it off |
| `GdiIcmGammaRange` under `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM` | `256` | You explicitly lift the gamma range limit in Settings › Startup and permissions; requires administrator rights and triggers a UAC prompt |

Both can be reverted at any time: turn the switch off, or delete the value manually.

## What the app does not do

- It does not collect, upload, sell, or share any personal information or usage data;
- It contains no advertising, trackers, analytics SDK, crash reporting, or automatic update check;
- It does not read your documents, images, browsing history, chat contents, or the input of other programs;
- It does not read or modify the memory or files of any game or other program — it only changes the graphics card gamma LUT and monitor OSD parameters, both of which are display output;
- It creates no account, requires no sign-in, and needs no network connection for any feature.

## Retention and deletion

- Settings and baselines all live in `GammaLiftSettings.json`. Deleting that file clears every setting (defaults are rebuilt on the next launch, and baselines are captured again).
- Deleting `GammaLift.exe` or simply stopping use of the app does **not** delete that file; remove it yourself if you want it gone.
- Registry values are reverted as described in the table above.
- The app writes no logs and creates no cache, so there are no other runtime traces to clean up.

## Third-party components

The app bundles no third-party library or embedded SDK. It calls only components that ship with Windows, plus assemblies that ship with .NET Framework (for example `System.Web.Extensions` for JSON serialization):

- **GDI32**: `GetDeviceGammaRamp` / `SetDeviceGammaRamp`, reading and writing the gamma LUT of each monitor output;
- **User32**: monitor enumeration, global hotkey registration, keyboard hook;
- **Dxva2**: DDC/CI monitor configuration (VCP codes `0x10` / `0x12` / `0x8A`).

Those system components are subject to Microsoft's terms and privacy policy. The app is rebuilt from [GammaPanelX](https://github.com/NiseMonox/GammaPanelX) (MIT licensed); see [LICENSE](../LICENSE) for the license and attribution.

## Children's privacy

The app is a display adjustment tool. It is not designed for children and collects no age or identity information — it collects no information at all.

## Policy updates

If the app's data handling changes (for example, if an optional update check is added later), this document will be updated first, with a new effective date and a change record in the repository. Until then, the behavior described here applies.

## Contact

For questions about this policy, or if you find that the app's actual behavior differs from this document, please report it through the Issues channel of the project page — we treat such discrepancies as bugs to fix.
