<#
    GoTweaks Lite - GUI end-user installer (source for the compiled GoTweaks-Setup.exe).

    This is the GUI counterpart of "Install GoTweaks.ps1" - same steps, same error handling,
    but driven by a small WinForms status window + message boxes instead of a console, and
    compiled to a single double-clickable .exe via ps2exe (see Build-Release.ps1).

    Elevation is handled by the compiled exe's own manifest (ps2exe -requireAdmin), so this
    script does NOT self-relaunch like the console version - Windows shows the UAC prompt
    before any of this code runs at all.

    The .msixbundle and .cer are EMBEDDED in the compiled exe (ps2exe -embedFiles, see
    Build-Release.ps1) and extracted to %TEMP%\GoTweaksSetup\ at startup, so the whole install
    is a single downloaded file - nothing needs to sit next to the exe. The -BundlePath/
    -CertPath params below are a manual override (dev convenience: running the raw .ps1, or a
    rebuild without -embedFiles); if omitted, the script looks for the extracted copies first,
    then falls back to searching next to the exe.

    What it does (and why it needs admin):
      1. Imports the bundled .cer into the machine's Trusted People + Trusted Root stores -
         required for Windows to accept a self-signed MSIX. This is the only reason admin
         is needed here.
      2. Closes Game Bar / the GoTweaks helper if running (they hold the old package open
         and would cause error 0x80073D02).
      3. Installs / updates the package for the current user.

    No data is sent anywhere. Everything runs locally.
#>

[CmdletBinding()]
param(
    # Optional explicit paths; auto-detected (embedded copy, then the exe's own folder) if omitted.
    [string]$BundlePath,
    [string]$CertPath
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

# --- Status window ----------------------------------------------------
$form = New-Object System.Windows.Forms.Form
$form.Text = "GoTweaks Lite Setup"
$form.Size = New-Object System.Drawing.Size(420, 140)
$form.StartPosition = "CenterScreen"
$form.FormBorderStyle = 'FixedDialog'
$form.MaximizeBox = $false
$form.MinimizeBox = $false
$form.TopMost = $true

$label = New-Object System.Windows.Forms.Label
$label.Text = "Starting..."
$label.AutoSize = $false
$label.Size = New-Object System.Drawing.Size(380, 30)
$label.Location = New-Object System.Drawing.Point(16, 16)
$form.Controls.Add($label)

$progress = New-Object System.Windows.Forms.ProgressBar
$progress.Style = 'Marquee'
$progress.MarqueeAnimationSpeed = 30
$progress.Size = New-Object System.Drawing.Size(380, 20)
$progress.Location = New-Object System.Drawing.Point(16, 56)
$form.Controls.Add($progress)

$form.Show()
$form.Refresh()

function Set-Status([string]$msg) {
    $label.Text = $msg
    $form.Refresh()
    [System.Windows.Forms.Application]::DoEvents()
}

function Fail([string]$msg) {
    $form.Close()
    [System.Windows.Forms.MessageBox]::Show($msg, "GoTweaks Lite Setup - Error", `
        [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
    exit 1
}

# --- Locate the package + certificate ----------------------------------
# The exe's own folder, regardless of how ps2exe resolves $PSScriptRoot internally.
$here = Split-Path -Parent ([System.Diagnostics.Process]::GetCurrentProcess().MainModule.FileName)
# Where ps2exe -embedFiles extracts the embedded bundle/cert at startup (must match the
# target paths given to -embedFiles in Build-Release.ps1).
$embeddedDir = Join-Path $env:TEMP "GoTweaksSetup"

Set-Status "Locating the package..."

if (-not $BundlePath) {
    $embeddedBundle = Join-Path $embeddedDir "GoTweaks.msixbundle"
    if (Test-Path $embeddedBundle) {
        $BundlePath = $embeddedBundle
    } else {
        # Dev convenience: same folder as the exe, or a raw uncompiled run of this script.
        $BundlePath = Get-ChildItem -Path $here -Filter *.msixbundle -ErrorAction SilentlyContinue |
                      Sort-Object LastWriteTime | Select-Object -Last 1 -ExpandProperty FullName
        if (-not $BundlePath) {
            $appPkgs = Join-Path $here '..\XboxGamingBarPackage\AppPackages'
            if (Test-Path $appPkgs) {
                $BundlePath = Get-ChildItem -Path $appPkgs -Recurse -Filter *.msixbundle -ErrorAction SilentlyContinue |
                              Sort-Object LastWriteTime | Select-Object -Last 1 -ExpandProperty FullName
            }
        }
    }
}
if (-not $BundlePath -or -not (Test-Path $BundlePath)) {
    Fail "No .msixbundle found (embedded copy missing, and none next to Setup.exe either)."
}

if (-not $CertPath) {
    $embeddedCert = Join-Path $embeddedDir "GoTweaks.cer"
    if (Test-Path $embeddedCert) {
        $CertPath = $embeddedCert
    } else {
        $bundleDir = Split-Path -Parent $BundlePath
        $CertPath = Get-ChildItem -Path $bundleDir -Filter *.cer -ErrorAction SilentlyContinue |
                    Select-Object -First 1 -ExpandProperty FullName
        if (-not $CertPath) {
            $CertPath = Get-ChildItem -Path $here -Filter *.cer -ErrorAction SilentlyContinue |
                        Select-Object -First 1 -ExpandProperty FullName
        }
    }
}
if (-not $CertPath -or -not (Test-Path $CertPath)) {
    Fail "No .cer certificate found (embedded copy missing, and none next to the package either)."
}

# --- 1. Trust the signing certificate -----------------------------------
Set-Status "Trusting the signing certificate..."
try {
    Import-Certificate -FilePath $CertPath -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
    Import-Certificate -FilePath $CertPath -CertStoreLocation Cert:\LocalMachine\Root        | Out-Null
} catch {
    Fail "Failed to import the certificate: $($_.Exception.Message)"
}

# --- 2. Close anything holding the old package open ----------------------
Set-Status "Closing Game Bar / GoTweaks helper if running..."
try { schtasks.exe /End /TN "GoTweaks\GoTweaksHelper" 2>&1 | Out-Null } catch {}
Get-Process XboxGamingBarHelper, GameBar, GameBarFTServer, GameBarPresenceWriter `
    -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 3

# --- 3. Install / update the package --------------------------------------
Set-Status "Installing GoTweaks Lite..."
function Install-Bundle { Add-AppxPackage -Path $BundlePath -ForceUpdateFromAnyVersion -ErrorAction Stop }

try {
    Install-Bundle
} catch {
    if ("$_" -match '0x80073D02') {
        Set-Status "Package is in use - closing blockers again and retrying..."
        Get-Process XboxGamingBarHelper, GameBar, GameBarFTServer, GameBarPresenceWriter `
            -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 5
        try { Install-Bundle }
        catch { Fail "Install failed even after retry. Close the Game Bar overlay (or sign out and back in) and run this again.`n$($_.Exception.Message)" }
    }
    elseif ("$_" -match '0x800B0109') {
        Fail "Windows still does not trust the certificate (0x800B0109). Try running the installer again."
    }
    else {
        Fail "Install failed: $($_.Exception.Message)"
    }
}

# Clean up the extracted embedded copies - only relevant when they came from $embeddedDir,
# harmless no-op otherwise (Remove-Item on a path that isn't there / isn't ours).
if ($BundlePath -like "$embeddedDir\*") {
    try { Remove-Item $embeddedDir -Recurse -Force -ErrorAction SilentlyContinue } catch {}
}

$form.Close()
[System.Windows.Forms.MessageBox]::Show(
    "GoTweaks Lite installed.`n`nOpen the Game Bar (Win + G), click the widget menu, and add GoTweaks.",
    "GoTweaks Lite Setup",
    [System.Windows.Forms.MessageBoxButtons]::OK,
    [System.Windows.Forms.MessageBoxIcon]::Information) | Out-Null
