[简体中文](README.md) | **English**

# GammaLift

Lift the shadows, keep the picture. Per-monitor gamma, brightness, contrast and saturation for Windows.

![GammaLift main window](docs/screenshots/main-en.png)

A modern remake of the classic [Gamma Panel](https://gamma-panel.en.softonic.com/), rebuilt from [GammaPanelX](https://github.com/NiseMonox/GammaPanelX) with one key improvement: every monitor can be selected and adjusted independently (the original only affected the primary display).

Portable single-file app — no installer, no .NET SDK or runtime downloads (uses the .NET Framework 4.x that ships with Windows).

The UI ships in 16 languages (Simplified / Traditional Chinese, English, Japanese, Korean, German, French, Spanish, Brazilian Portuguese, Russian, Italian, Turkish, Vietnamese, Indonesian, Dutch, Polish) and follows the system language by default.

## Download

Grab `GammaLift.exe` from the [Releases](../../releases) page and double-click to run. Requires Windows 10/11 with the built-in .NET Framework 4.8; the exe is AnyCPU and self-contained.

SmartScreen: the binary is unsigned, so Windows may warn on first run. Choose *More info → Run*, or check the SHA256 published alongside the release, or [build from source](#building) yourself.

## Features

- Per-monitor brightness / contrast / gamma / saturation, each display independent
- Two channels: software gamma LUT plus hardware DDC/CI; saturation goes to the monitor panel
- Reorder, rename or delete profiles in one **Edit…** dialog; the order carries through the drop-down, tray menu and cycle hotkey
- Rename profiles and share one display's software parameters with live 11–22 character offline codes
- Exclusive global hotkeys support single press, double press and long press, plus cycling through profiles
- Master notification switch with separate profile-applied and background-running controls
- Baseline protection: reset restores your system ICC/vcgt calibration exactly
- The top **Help › About** dialog shows the version, both copyright lines, the full MIT text and the project repository
- Portable single exe, 16 UI languages, tray resident; minimize can leave only the notification-area icon

Detailed feature notes, tips and technical background live in [docs/guide.md](docs/guide.md) (Chinese).

The share-code box under the profile section updates live for the selected display and stays empty at default settings. A linked code is 11 characters, a per-channel code 22. Copy it from the box; paste someone else's code and click **Import** to apply it to the current display immediately (it is not saved to a profile until you click **Save**).

The cycle-hotkey row above the profile section binds cycling through the profile list and wraps to the beginning. Long press triggers after 400 ms, and double press uses the Windows double-click interval. A single press waits if the same key also has a double- or long-press binding. **Settings → Tray and notifications** controls the existing custom toast windows, not Windows native notifications; errors and save confirmations remain visible.

## Use cases

- Dark-scene games: titles such as Escape from Tarkov push interiors and night maps close to black, and a higher gamma with a touch of brightness separates shapes in a stairwell or under foliage while highlights stay roughly where they were. Turn on periodic re-apply in Settings if the game or the driver keeps resetting the gamma LUT.
- Reading at night: lower the brightness and pull gamma down slightly so a white page stops glaring. The change applies only to the monitor holding the document; the second screen keeps its own settings.
- Underexposed photos: raise gamma temporarily to see whether a dark frame holds recoverable detail or just noise. The image file is untouched, and Reset returns the display to the captured system ICC/vcgt baseline.
- Badly mastered video: older films and low-bitrate encodes often collapse the shadows into one black patch. More gamma spreads those tones apart, so set detail can be told from blocking left by the encode.
- Mismatched monitors: one panel runs dim while another looks oversaturated; adjust brightness and contrast per monitor, and saturation over DDC/CI where the panel supports it. Settings are keyed by monitor hardware ID, so switching ports or replugging a cable does not mix them up.
- Moving between setups: save Day, Night and shadow-lift as profiles, each covering every monitor, and bind a global hotkey such as Ctrl+Alt+1 to switch.

GammaLift only changes the display output (GPU gamma LUT or monitor OSD); it never reads or modifies game memory or files.

## Building

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
# Output: build\GammaLift.exe
```

No .NET SDK required — it compiles with the `csc.exe` bundled with Windows (which is why the source sticks to C# 5 syntax).

## Lineage & credits

GammaLift stands on two shoulders:

- The classic [Gamma Panel](https://gamma-panel.en.softonic.com/).
- [GammaPanelX](https://github.com/NiseMonox/GammaPanelX) by [NiseMonox](https://github.com/NiseMonox) (2026-07), the multi-monitor remake this repository descends from. Those original commits remain in this repository's history under their original authorship.

### Original Gamma Panel history

According to the ReadMe bundled with the original **Gamma Panel 1.0**, the author was **Tomasz Porosinski**, with the copyright notice **Copyright 2002–2003 Tomasz Porosinski**. Its version history identifies **1.0.0.20 (December 1, 2002)** as the first released version; the end of the ReadMe is also dated `01.12.2002 12:00 (GMT +01:00)`.

The original was designed to adjust brightness, contrast and gamma in real time. Profiles could be applied with hotkeys or from the tray pop-up menu, and added, modified or removed in the main window. The ReadMe describes it as available free of charge for personal use only and permits redistribution only when free and unmodified. The material provides neither source code nor an open-source license, so it cannot confirm whether the original was open source; no open-source grant is evident from this material.

The GammaLift rename, the UI rework, the settings dialog, the 16-language support, the toast notifications, the curve preview and the settings/auto-start migration are by [suiying8345](https://github.com/suiying8345). The MIT license and both copyright lines are preserved — see [LICENSE](LICENSE).

Settings live next to `GammaLift.exe` as `GammaLiftSettings.json` (falling back to your Documents folder when the exe directory is not writable). On first launch an existing configuration under `%APPDATA%` (GammaLift, Lumin or GammaPanelX) and the startup entry are carried over; the old folders are left untouched.

## Privacy

GammaLift makes no network connections, needs no account, and contains no ads or analytics. All settings stay on your machine in `GammaLiftSettings.json`. See the [privacy policy](docs/privacy-policy.en.md) for details.

## License

[MIT](LICENSE)