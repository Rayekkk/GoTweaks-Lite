<#
    GoTweaks Lite - end-user installer
    ----------------------------------
    Self-signed sideload install (distribution option "C").

    Place this script in the SAME folder as:
        GoTweaks_<version>.msixbundle   (the app package)
        GoTweaks_<version>.cer          (the signing certificate)

    Then right-click -> "Run with PowerShell", or double-click
    "Install GoTweaks.bat". The script self-elevates (one UAC prompt),
    trusts the certificate, and installs the package.

    What it does (and why it needs admin):
      1. Imports the bundled .cer into the machine's Trusted People +
         Trusted Root stores - required for Windows to accept a
         self-signed MSIX. This is the only reason admin is needed here.
      2. Closes Game Bar / the GoTweaks helper if running (they hold the
         old package open and would cause error 0x80073D02).
      3. Installs / updates the package for the current user.

    No data is sent anywhere. Everything runs locally.
#>

[CmdletBinding()]
param(
    # Optional explicit paths; auto-detected from the script folder if omitted.
    [string]$BundlePath,
    [string]$CertPath
)

$ErrorActionPreference = 'Stop'

# --- Self-elevate ---------------------------------------------------
$identity  = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "Requesting administrator rights (needed to trust the certificate)..." -ForegroundColor Yellow
    $psExe = (Get-Process -Id $PID).Path   # pwsh.exe or powershell.exe, whichever launched us
    $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"")
    if ($BundlePath) { $argList += @('-BundlePath', "`"$BundlePath`"") }
    if ($CertPath)   { $argList += @('-CertPath',   "`"$CertPath`"") }
    try {
        Start-Process -FilePath $psExe -Verb RunAs -ArgumentList $argList
    } catch {
        Write-Host "Elevation was cancelled. Installation aborted." -ForegroundColor Red
        Read-Host "Press Enter to close"
    }
    return
}

function Write-Step($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }
function Fail($msg) {
    Write-Host ""
    Write-Host "ERROR: $msg" -ForegroundColor Red
    Read-Host "Press Enter to close"
    exit 1
}

Write-Host ""
Write-Host "  GoTweaks Lite installer" -ForegroundColor Green
Write-Host "  =======================" -ForegroundColor Green
Write-Host ""

# --- Locate the package + certificate -------------------------------
$here = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }

if (-not $BundlePath) {
    $BundlePath = Get-ChildItem -Path $here -Filter *.msixbundle -ErrorAction SilentlyContinue |
                  Sort-Object LastWriteTime | Select-Object -Last 1 -ExpandProperty FullName
    # Dev convenience: fall back to the build output folder if not next to the script.
    if (-not $BundlePath) {
        $appPkgs = Join-Path $here '..\XboxGamingBarPackage\AppPackages'
        if (Test-Path $appPkgs) {
            $BundlePath = Get-ChildItem -Path $appPkgs -Recurse -Filter *.msixbundle -ErrorAction SilentlyContinue |
                          Sort-Object LastWriteTime | Select-Object -Last 1 -ExpandProperty FullName
        }
    }
}
if (-not $BundlePath -or -not (Test-Path $BundlePath)) {
    Fail "No .msixbundle found next to this script. Make sure the package file is in the same folder."
}

if (-not $CertPath) {
    # Prefer a .cer sitting beside the chosen bundle.
    $bundleDir = Split-Path -Parent $BundlePath
    $CertPath = Get-ChildItem -Path $bundleDir -Filter *.cer -ErrorAction SilentlyContinue |
                Select-Object -First 1 -ExpandProperty FullName
    if (-not $CertPath) {
        $CertPath = Get-ChildItem -Path $here -Filter *.cer -ErrorAction SilentlyContinue |
                    Select-Object -First 1 -ExpandProperty FullName
    }
}
if (-not $CertPath -or -not (Test-Path $CertPath)) {
    Fail "No .cer certificate found next to the package. It is required to trust the self-signed package."
}

Write-Host "Package: $(Split-Path -Leaf $BundlePath)"
Write-Host "Cert:    $(Split-Path -Leaf $CertPath)"
Write-Host ""

# --- 1. Trust the signing certificate -------------------------------
Write-Step "Trusting the signing certificate"
try {
    Import-Certificate -FilePath $CertPath -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
    Import-Certificate -FilePath $CertPath -CertStoreLocation Cert:\LocalMachine\Root        | Out-Null
} catch {
    Fail "Failed to import the certificate: $($_.Exception.Message)"
}

# --- 2. Close anything holding the old package open -----------------
Write-Step "Closing Game Bar / GoTweaks helper if running"
try { schtasks.exe /End /TN "GoTweaks\GoTweaksHelper" 2>&1 | Out-Null } catch {}
Get-Process XboxGamingBarHelper, GameBar, GameBarFTServer, GameBarPresenceWriter `
    -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 3

# --- 3. Install / update the package --------------------------------
Write-Step "Installing the package"
function Install-Bundle { Add-AppxPackage -Path $BundlePath -ForceUpdateFromAnyVersion -ErrorAction Stop }

try {
    Install-Bundle
} catch {
    if ("$_" -match '0x80073D02') {
        Write-Host "   Package is in use - closing blockers again and retrying..." -ForegroundColor Yellow
        Get-Process XboxGamingBarHelper, GameBar, GameBarFTServer, GameBarPresenceWriter `
            -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 5
        try { Install-Bundle }
        catch { Fail "Install failed even after retry. Close the Game Bar overlay (or sign out and back in) and run this again.`n$($_.Exception.Message)" }
    }
    elseif ("$_" -match '0x800B0109') {
        Fail "Windows still does not trust the certificate (0x800B0109). Try running this installer again."
    }
    else {
        Fail "Install failed: $($_.Exception.Message)"
    }
}

Write-Host ""
Write-Host "  Done - GoTweaks Lite installed." -ForegroundColor Green
Write-Host "  Open the Game Bar (Win + G), click the widget menu, and add GoTweaks." -ForegroundColor Green
Write-Host ""
Read-Host "Press Enter to close"
