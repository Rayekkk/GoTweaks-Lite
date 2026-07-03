<div align="center">

# 🎮 GoTweaks Lite

**A stripped-down, Lenovo Legion Go 2–focused fork of [GoTweaks](https://github.com/corando98/GoTweaks).**

An Xbox Game Bar widget for controlling TDP, fans, RGB, controller remapping, gyro, per-game
profiles, and an OSD — rebuilt into a clean, predictable base tuned specifically for the
**Legion Go 2 (AMD Ryzen Z2 Extreme)**.

`C#` · `Xbox Game Bar widget` · `MIT source / GPL-3.0 binaries`

</div>

> [!NOTE]
> **Installation differs from the original.** Use the [`Installer/`](Installer/) folder
> (run **`Install GoTweaks.bat`**) — not the upstream download-and-run-`Install.ps1` steps.

---

## 📑 Contents

- [What's different from the original](#-whats-different-from-the-original)
- [Features](#-features)
- [Installation](#-installation)
- [Requirements](#-requirements)
- [Technology](#-technology)
- [Credits & License](#-credits)

---

## ✨ What's different from the original

This is the fork's identity — everything below is what GoTweaks Lite changes relative to
upstream, and why.

### 🎯 Focus

- **Legion Go 2 first.** The build is tuned for the Legion Go 2 (Ryzen Z2 Extreme). Other
  handhelds may still work, but they aren't a priority — decisions optimize for the Legion Go 2.

### ⚡ Rebuilt TDP control

- **One _TDP Mode_ selector** — Quiet / Balanced / Performance (Lenovo firmware presets) +
  **Custom**, replacing the old sprawling TDP UI and the duplicate Legion-tab power-profile dropdown.
- **Custom = base TDP (SPL) + SPPT / FPPT boosts** — clamped to a 50 W safety ceiling and applied
  **live** over Lenovo WMI. Mirrors Legion Space semantics and removes the old dual-write-path
  conflicts that used to snap limits back to ~15 W.
- **Removed the standalone master TDP slider** — folded into the mode selector.

### 🧹 Removed for a leaner base

- **AutoTDP** (the Q-learning / SARSA power controller), **Sticky TDP**, the **TDP Boost** toggle,
  **Custom TDP Presets**, and the **Device Min/Max TDP** panel — the single _TDP Mode_ selector
  replaces them all.
- The beta **Sidebar overlay** — _Focus GoTweaks_ now simply opens the Game Bar.
- **Microsoft / bundled Default Game Profiles** — only your own per-game profiles remain.
- The **Advanced panel** (core parking / affinity), the **AC/DC Power Plan** selector, and the debug
  **Themes** selector — niche or no-ops on the Legion Go 2.

> **Why:** a smaller, more predictable surface with fewer background systems that can silently
> fight your settings.

### ➕ Added

- **Auto SDR** — while HDR is on, automatically matches the SDR white level to screen brightness
  so SDR content (desktop, most games) doesn't look washed out. _(Ported from the sibling Go2HDR
  project.)_
- **PresentMon-based OSD metrics** — real rendered vs displayed FPS, a `[FG]` frame-generation
  badge, and a frame-budget readout.
- **Fix Task View Bug** _(Labs, opt-in)_ — a targeted fix for the Legion Go bug where, after a
  restart with a USB hub attached, focusing the desktop pops open Task View (Win+Tab) and buzzes
  the controller. It re-enumerates the controller's USB port once per boot — the software
  equivalent of physically replugging a pad. Enable it only if you have this bug.

### 🔧 Reliability fixes

- Correct **system-tray icon** (was the generic Windows icon).
- Fixed **swapped CPU/GPU wattage sensors** on the Legion Go 2 so OSD labels match Adrenalin.
- More reliable **AFMF toggle**, **fan-curve temperature** (now true CPU Tctl, not the chipset
  sensor), and **Fan Full Speed**.
- **Controller vibration & lighting persistence** across restarts, a **24-hour OSD clock**, extra
  navigation / media keys in the remap pickers, and improved **Lossless Scaling** runtime
  detection & reliability.

### 🔄 In-app updates (points at this fork)

- **GoTweaks Lite updates itself from _this_ repo** — never the upstream (corando98) build. When a
  newer release lands here, a banner offers to download and install it in place, in one tap.
- **One unified update path** — a single check in the elevated helper looks at this repo's latest
  release and installs the signed `.msixbundle` silently. The **Check for updates on start** toggle
  and the **Check for Update** button in Settings both use it.
- **Release number ≠ build number, and that's fine** — releases carry a friendly label (e.g. `1.0`)
  while the package has an auto-incrementing build version (e.g. `0.3.2492.0`). The updater compares
  the _build_ version, so the friendly release tag never confuses it.

---

## 🧩 Features

### 🗂️ Quick Settings

A customizable dashboard of quick-access tiles for your most-used settings.

- One-tap toggles for TDP Mode, Profile, Overlay, and Lossless Scaling
- Custom keyboard-shortcut tiles you can add and remove
- Device-specific tiles that appear when supported hardware is detected

### ⚡ Performance Control

Fine-tune power and CPU settings for performance or battery life.

**TDP management**
- **TDP Mode** — Quiet / Balanced / Performance firmware presets, plus Custom
- **Custom Power Limits** — base TDP (SPL) with independent SPPT and FPPT boosts, a 50 W ceiling,
  and live apply while dragging
- **Live readout** — current SPL / SPPT / FPPT confirmed straight from the hardware

**CPU controls**
- **CPU Boost** — enable or disable CPU boost
- **CPU EPP** — Energy Performance Preference (0–100)
- **Min / Max CPU State** — control the CPU clock-speed range

### 🎯 Per-Game Profiles

Automatically apply your preferred settings when each game launches.

- Automatic profile switching on game detection
- Saves all performance settings per game:
  - TDP (SPL / SPPT / FPPT) and CPU settings
  - AMD Radeon features
  - Lossless Scaling configuration
  - Legion Go controller settings

### 🕹️ Legion Go Support

Deep support for the Legion Go 2 (and other Legion Go handhelds) with automatic device detection.

**Performance modes**
- Quiet, Balanced, Performance, and Custom
- Custom TDP with fine-grained control (SPL, SPPT, FPPT)
- Fan Full Speed toggle

**Controller settings**
- **Button Remapping** — customize the M2, M3, Y1, Y2, Y3 buttons
- **Remap type** — map to keyboard shortcuts, keys, gamepad actions, or mouse buttons
- **Joystick to Mouse** — emulate a mouse with either stick
- **Desktop Controls** — map controls to mouse + left/right click, similar to the ROG Ally
- **Nintendo Layout** — swap the button layout
- **Stick Deadzones** — adjust left/right stick deadzones (0–50%)
- **Gyroscope** — enable/disable with target selection, X/Y sensitivity, axis inversion, and
  activation mode (Hold/Toggle) with a button binding
- **Vibration Mode** — configure vibration behavior
- **Touchpad Haptics** — toggle haptic feedback

**RGB lighting**
- Light mode, color, brightness, and speed control
- Power-light toggle

**Other**
- Touchpad toggle
- Battery charge limit

### 🔴 AMD Radeon Features

GPU-based enhancements for AMD graphics.

| Category | Feature |
| --- | --- |
| **Upscaling & frame gen** | Radeon Super Resolution (RSR) with sharpness · AMD Fluid Motion Frames (AFMF) |
| **Performance** | Radeon Anti-Lag · Radeon Boost (dynamic resolution) |
| **Power saving** | Radeon Chill with min/max FPS |
| **Image quality** | Image Sharpening · display color controls (brightness, contrast, saturation, temperature) |

### 🖼️ Lossless Scaling Integration

Control Lossless Scaling directly from the widget _(the Scale tab appears when Lossless Scaling is
detected)_.

- Launch and manage Lossless Scaling
- Configure scaling type and factor
- Frame-generation modes (LSFG2, LSFG3)
- Auto-scaling with configurable delay
- Anime4K and FSR optimization options
- Per-profile configurations

### 🖥️ Graphics Settings

Display and resolution management.

- Resolution control with auto-detection
- Refresh-rate adjustment
- HDR toggle (when supported)
- **Auto SDR** — match the SDR white level to screen brightness while HDR is active

### 📊 Performance Overlay (OSD)

A real-time on-screen display powered by RivaTuner Statistics Server.

- **Detail levels** — Off, Minimal, Standard, Detailed
- **Metrics** — FPS (rendered + displayed via PresentMon) with a `[FG]` badge, frametime graph and
  frame-budget readout, CPU/GPU usage & temperatures, power draw, memory & VRAM, battery, fan speed,
  and TDP limits (SPL/SPPT/FPPT) or the current performance mode

### 🎮 Controller Navigation

Designed for full gamepad control — no mouse needed.

- D-pad navigation between all controls
- Visual focus indicators
- Automatic scroll to the focused item
- Works with Xbox, PlayStation, and other controllers

---

## 📦 Installation

GoTweaks Lite ships as a sideloaded MSIX package signed with a self-signed certificate, so the
installer first tells Windows to trust that certificate.

### 1. Install

1. Grab the latest release — a folder/zip with `GoTweaks_<version>.msixbundle`, its matching
   `.cer`, and the installer. **Keep all files in the same folder.**
2. **Close the Game Bar overlay** if it's open (`Win + G` to check) — an open Game Bar keeps the
   old version running and blocks the update.
3. Double-click **`Install GoTweaks.bat`**.
4. Click **Yes** on the UAC prompt — needed only to trust the certificate.
5. Wait for **“Done — GoTweaks Lite installed.”**

> If double-clicking the `.bat` is blocked, right-click **`Install GoTweaks.ps1`** →
> **Run with PowerShell**.

The installer trusts the certificate, closes blocking processes, and installs/updates the widget in
place — your profiles and settings are kept.

### 2. Enable the widget

1. Open Xbox Game Bar (`Win + G`)
2. Open the **widget menu**
3. Find and enable **“GoTweaks”**

> The **first launch shows one more UAC prompt** — the elevated helper that drives the hardware
> (TDP, fans, RGB) needs administrator rights. After that it registers a scheduled task, so future
> launches are silent.

### 3. Enable game detection

Required for per-game profiles:

1. Open Xbox Game Bar → **Settings** → **More Settings**
2. Find the **GoTweaks** widget
3. Enable **“Know which game or app is in focus”**

---

## ✅ Requirements

| | |
| --- | --- |
| **OS** | Windows 10 / 11 |
| **Required** | Xbox Game Bar |
| **Optional** | [RivaTuner Statistics Server](https://www.guru3d.com/download/rtss-rivatuner-statistics-server-download/) (OSD overlay) · [PawnIO](https://github.com/SuporteTI/PawnIO) (extended sensors: fan speed, GPU power) · AMD GPU (Radeon features) · Legion Go 2 / Legion Go (device features) · Lossless Scaling (scaling integration) |

> [!IMPORTANT]
> **Smart App Control** may interfere with this application. If it doesn't work correctly, you may
> need to disable Smart App Control in Windows Security. _(Other handheld software like ASUS Armoury
> Crate hits the same Smart App Control issue.)_

---

## 🛠️ Technology

Free and open source. Built with C#.

| Library | Purpose |
| --- | --- |
| **LibreHardwareMonitor** | Hardware sensors and monitoring |
| **RyzenAdj** | AMD TDP control |
| **RTSSSharedMemoryNET** | Custom build with frametime-graph support, tuned for low CPU/memory use |
| **ADLX** | AMD Display Library for Radeon features |
| **PresentMon** | Rendered / displayed FPS and frame-generation metrics |

---

## 🙏 Credits

GoTweaks Lite builds on the work of the upstream projects:

- **[GoTweaks](https://github.com/corando98/GoTweaks)** by corando98 — the fork this project is based on.
- **[Original widget](https://github.com/namquang93)** by namquang93 — where GoTweaks itself grew from.

## 📄 License

GoTweaks Lite has a layered license, inherited from upstream:

- The **original source code** is licensed under the **MIT License** (see [`LICENSE`](LICENSE)).
- The **distributed binaries** link `libviiper.dll` (a GPL-3.0 fork of VIIPER), so the
  **combined work as distributed is conveyed under the GPL-3.0** (see [`COPYING`](COPYING)).

See [`LICENSING.md`](LICENSING.md) and [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md) for the full
details and the written offer of source.
