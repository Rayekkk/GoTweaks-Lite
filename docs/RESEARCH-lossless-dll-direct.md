# Direct Lossless.dll integration — research notes

Branch: `feature/lossless-dll-direct`. Read this first; no code changes yet.

## Goal

Replace the current GoTweaks Lossless Scaling integration (edit `Settings.xml`, kill+launch `LosslessScaling.exe`, inject hotkey) with direct calls into `Lossless.dll`. Primary features wanted: upscale + frame generation. The motivating wins are:

- No `LosslessScaling.exe` taskbar entry / process running
- Per-profile changes apply instantly (no app kill+restart)
- No hotkey timing race — settings + activate are explicit calls

## Current state

`XboxGamingBarHelper/LosslessScaling/LosslessScalingManager.cs:1001` — `SaveAndRestart` writes `Settings.xml` then calls `proc.Kill()` on `LosslessScaling.exe`, waits 1 s, relaunches. That's the friction we want to remove.

Current property surface (from `Shared/Enums/Function.cs:60-84`) covers everything LS exposes: scaling type / sharpness / FSR-optimize / Anime4K size+VRS / scale mode+factor / aspect / FG type (LSFG1/2/3) / LSFG3 mode+multiplier+target / LSFG2 mode / flow scale / size / autoscale + delay. So we already model the full settings surface — we just deliver it via XML+hotkey instead of an API call.

## What LosslessProxy taught us

[FrankBarretta/LosslessProxy](https://github.com/FrankBarretta/LosslessProxy) is **not** a standalone DLL driver. It's a DLL-replacement *proxy* that sits between `LosslessScaling.exe` and the original `Lossless.dll`, forwarding all calls and adding an addon system for shader replacement and event hooks. It still requires `LosslessScaling.exe` to be the host process. Architecture is C++17 with linker-level export forwarding via `#pragma comment(linker, "/export:...=Lossless_original.X")`.

What it gave us: **the full export list of `Lossless.dll`** (`LosslessProxy/src/core/proxy_exports.h` + `main.cpp`):

| Export | Signature | Notes |
|---|---|---|
| `Init` | unknown | — |
| `UnInit` | unknown | — |
| `Activate` | unknown | likely takes target HWND |
| **`ApplySettings`** | **known** (32 params, see below) | manually intercepted in proxy `main.cpp:20-27` |
| `SetDriverSettings` | unknown | GPU driver tweaks |
| `SetWindowsSettings` | unknown | Windows compat tweaks |
| `GetAdapterNames` | unknown | enumerate GPUs |
| `GetDisplayNames` | unknown | enumerate displays |
| `GetDwmRefreshRate` | unknown | DWM refresh-rate query |
| `GetForegroundWindowEx` | unknown | LS's foreground-window picker |
| `IsWindowsBuildAtLeast` | unknown | Windows version check |

`ApplySettings` signature, in C# P/Invoke shape (from `LosslessProxy/src/core/main.cpp:20-27`):

```csharp
[UnmanagedFunctionPointer(CallingConvention.FastCall)]
delegate void ApplySettings(
    int scalingMode, int scalingFitMode, int scalingType, int scalingSubtype,
    float scaleFactor, byte resizeBeforeScale, int sharpness, byte vrs,
    int frameGenType, int frameGenSize, int frameGenMode, float frameGenMultiplier, float frameGenTarget,
    int frameGenFlowScale, byte clipCursor, byte adjustCursorSpeed, byte hideCursor, byte scaleCursor,
    int syncMode, int maxFrameLatency, byte gsyncSupport, byte hdrSupport,
    int captureApi, int queueTarget, byte drawFps, int gpuId, int displayId,
    int cropLeft, int cropTop, int cropRight, int cropBottom, byte multiDisplayMode);
```

This maps 1:1 to the existing GoTweaks property set — every field has an existing `LosslessScaling*Property`. Wiring the call site is mechanical *if* we have working `Init`/`Activate`/`UnInit` signatures.

Lossless.dll itself uses D3D11 internally — `LosslessProxy/src/core/d3d11_hook.cpp` patches `D3D11CreateDevice`, suggesting the DLL creates its own D3D11 device on `Init`. So we don't need to provide a render context; we provide settings + a target HWND and let the DLL drive its own pipeline.

## Two architectural paths, very different costs

### Path A — drive `Lossless.dll` exports directly (small if we can find signatures)

LoadLibrary the user's `Lossless.dll` from their Steam install, P/Invoke `Init` → `ApplySettings` → `Activate(hwnd)` → `UnInit`. The 32-param `ApplySettings` is solved. The unknowns are `Init`, `Activate`, `UnInit` — and whether the DLL can run at all without `LosslessScaling.exe` providing some side-channel state (registry, named pipe, shared memory).

To find those signatures:
1. Disassemble `LosslessScaling.exe`, follow the calls into the DLL, read register conventions and stack writes for `__fastcall` to recover param shapes. ~1-2 days of focused RE work for someone comfortable with IDA/Ghidra.
2. Or: write a logging proxy modeled on LosslessProxy that records every call with its actual arguments, run LS once with it, harvest signatures from the log. Cleaner, takes a day at most.

Risk: even with signatures, the DLL may have hidden dependencies on the host process (its working directory, named-pipe servers `LosslessScaling.exe` runs, registry keys it writes on startup). Each one is a debugging round-trip. Realistic estimate: **3-7 days from "signatures known" to "GoTweaks helper can drive the DLL end-to-end on one machine."** Plus QA across LSFG versions, multi-monitor, HDR, etc.

### Path B — extract shaders from `Lossless.dll`, run our own pipeline (large)

This is what [`PancakeTAS/lsfg-vk`](https://github.com/PancakeTAS/lsfg-vk) (Linux Vulkan) and [`FrankBarretta/LSFG-Android`](https://github.com/FrankBarretta/LSFG-Android) do. They parse `Lossless.dll`'s PE resource section to extract HLSL/SPIR-V compute shaders (300 of them, per LosslessProxy's docs), then build their own capture → interpolate → present pipeline using those shaders.

For Windows + GoTweaks helper, that means:
- `Vortice.Windows` or `SharpDX` for D3D11
- `Windows.Graphics.Capture` for window capture
- `IDXGISwapChain` / composition surface for present
- Reimplement the LSFG/LS1/FSR/Anime4K compute chains against extracted shaders, with per-algorithm parameter binding
- Test parity against LS itself

This is **3-6 months of work**, full-time. We'd be writing Lossless Scaling Light. Out of scope unless the project is willing to take on a major new component.

The redeeming feature of Path B: it's the architecture used publicly by the lsfg-vk community, including in plugins distributed by Decky, so the legal posture is established (user's DLL, their machine, no redistribution). Path A's posture is murkier.

## EULA / legal posture

`Lossless.dll` is a paid Steam product. We must NOT redistribute the DLL — both paths assume the user owns LS and the DLL is on their disk. Beyond that:

- **Path A** has no precedent we found. Calling undocumented exports of a paid app outside its host process is in EULA grey area. The LS EULA likely has a "no reverse engineering / no use outside the licensed application" clause — needs verification before we ship anything that calls `Init` directly. Ask in `#lossless-scaling` Discord first if we go this way.
- **Path B** has public precedent (lsfg-vk, LSFG-Android) and explicit "user provides their own DLL, we never distribute" framing. Lower legal risk.

Either way, GoTweaks shouldn't ship `Lossless.dll`. We always read it from the user's Steam install path.

## Smaller wins that don't require either path

If the cost of A or B is too high, the current architecture has gaps we can close incrementally without ever touching the DLL:

1. **Hide the LS window from taskbar** — `SetWindowLong(hWnd, GWL_EXSTYLE, WS_EX_TOOLWINDOW)` after launch makes it invisible to the taskbar. Already running, just not in the user's face.
2. **Don't kill+restart for every change** — check whether LS reloads `Settings.xml` on a config-file watcher. If yes, just write XML and the changes take effect. If not, we can `SendMessage(WM_APP, ...)` LS to ask it to reload — but that requires LS to listen for it, which it probably doesn't. Worst case, current behaviour stays.
3. **Pre-launch LS at helper start, keep minimized** — first time the user opens the Scaling tab is currently a 1-2 s wait while LS loads. Kicking it off in the background at helper start hides that latency.
4. **Per-game profile sync** — already partially done; tighten the "switch profile when game starts" path so the active LS profile always matches the running game without a user click.

Items (1) + (3) get us most of the perceived UX win of Path A — "LS is invisible, scaling just works" — at zero risk.

## My recommendation

**Don't go Path B.** It's not "improve the Scaling tab" — it's a six-month side project that duplicates a paid product.

**Path A is interesting but high-risk.** The single biggest unknown is whether the DLL actually works outside `LosslessScaling.exe`'s process — there's no public proof either way. I'd want to validate that before committing to even the signature-reverse work. Concrete validation experiment, ~1 day:

1. Write a tiny C++ harness that does `LoadLibrary("Lossless.dll")`, calls `Init` with no args, calls `Activate(GetForegroundWindow())`, calls `ApplySettings(...)` with reasonable defaults, sleeps 5 s, checks if anything actually scaled.
2. If the DLL refuses to initialize without the host (likely returns an error or silently no-ops), we have our answer. Stop there.
3. If it scales — even partially — we know the approach is feasible and the rest is signature reverse-engineering work.

Until we know (1)/(2)/(3), shipping the **smaller wins** above is the best use of time — they're low-risk, immediately user-visible, and don't gamble on undocumented APIs.

## Files referenced

- `XboxGamingBarHelper/LosslessScaling/LosslessScalingManager.cs:1001` — current kill+restart logic
- `Shared/Enums/Function.cs:60-84` — full LS property surface we already model
- `LosslessProxy/src/core/proxy_exports.h` — 10 forwarded exports (in extracted source at `/tmp/losslessproxy/`)
- `LosslessProxy/src/core/main.cpp:20-27` — `ApplySettings` signature
- `LosslessProxy/src/core/d3d11_hook.cpp:15-18` — proves DLL owns its own D3D11 device

## Open questions for you when you're back

1. Is the user-perceived problem "LS app is annoying to have running" or "settings changes are slow"? Different fixes optimize for each.
2. Do we have a budget for ~1 day of validation RE work to confirm Path A is even possible? If not, lean on the smaller wins.
3. Is there appetite for asking the LS dev directly via Discord whether driving `Lossless.dll` standalone is permitted? That answer alone could redirect the whole effort.
