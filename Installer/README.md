# Installing GoTweaks Lite

GoTweaks Lite is an **Xbox Game Bar widget** for the **Lenovo Legion Go 2**. It is
distributed as a sideloaded MSIX package signed with a self-signed certificate, so the
installer has to tell Windows to trust that certificate before installing the app.

## What you downloaded

A folder (or zip) containing three files:

| File | What it is |
|------|------------|
| `GoTweaks_<version>.msixbundle` | the application package |
| `GoTweaks_<version>.cer`        | the certificate Windows must trust |
| `GoTweaks-Setup.exe`            | the installer |

Keep all of them **in the same folder**.

## How to install

1. **Close the Game Bar overlay** if it is open (press `Win + G` to check). An open Game
   Bar keeps the old version running and will block the update.
2. Double-click **`GoTweaks-Setup.exe`**.
3. Click **Yes** on the Windows administrator (UAC) prompt — this is needed only to trust
   the certificate.
4. A small window shows progress; wait for the **"GoTweaks Lite installed"** confirmation.

> `GoTweaks-Setup.exe` is unsigned (no paid code-signing certificate), so Windows SmartScreen
> may flag it on first run - click **More info** → **Run anyway** to continue. This is normal
> for any freshly downloaded, unsigned executable and not specific to GoTweaks Lite.
>
> If you'd rather run the underlying steps yourself (or the .exe is blocked outright), clone
> the repo and run `Installer\Install GoTweaks.ps1` from the same folder as the bundle/cert -
> it does the exact same thing from a console instead of a GUI.

## Using it

Open the Game Bar with **`Win + G`**, open the **widget menu** (the icon that lists
widgets), and select **GoTweaks**. Pin it so it is one click away next time.

The **first launch shows one more UAC prompt** — the helper that talks to the hardware
(TDP, fans, RGB) needs administrator rights. After that it sets itself up to start without
prompting again.

## Why does it need administrator rights?

- **Once, in the installer:** to add the self-signed certificate to the Trusted Root /
  Trusted People store so Windows accepts the package.
- **Once, on first run:** the elevated helper controls power limits, fans and lighting,
  which require admin. It then registers a scheduled task so future launches are silent.

Nothing is uploaded anywhere — everything runs locally on your device.

## Updating

Download the newer release and run the installer again. It updates in place; your profiles
and settings are kept.

## Uninstalling

Settings → Apps → Installed apps → **GoTweaks** → Uninstall. (Search for "GoTweaks".)

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| `0x80073D02 (package in use)` | Close the Game Bar overlay, or sign out and back in, then run the installer before opening Game Bar. |
| `0x800B0109 (untrusted)` | Run the installer again — the certificate import sometimes needs a second pass. |
| Widget not listed in Game Bar | Make sure the install finished, then close and reopen Game Bar (`Win + G`). |
