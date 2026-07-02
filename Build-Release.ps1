<#
    Build-Release.ps1 — produce a distributable GoTweaks Lite release (option "C").

    Builds the signed MSIX bundle, then assembles a clean `dist/` folder
    containing everything an end user needs:

        dist/
          GoTweaks_<version>.msixbundle
          GoTweaks_<version>.cer
          Install GoTweaks.bat
          Install GoTweaks.ps1
          README.md
          GoTweaks_<version>.zip   (the four above, zipped — upload this to a GitHub Release)

    Usage (from the repo root, in a Developer PowerShell / VS-enabled shell):
        ./Build-Release.ps1
        ./Build-Release.ps1 -Thumbprint <SHA1>     # override signing cert
        ./Build-Release.ps1 -SkipBuild             # just re-assemble dist from existing AppPackages

    Signing: by default the script finds a self-signed cert with subject "CN=dg"
    in Cert:\CurrentUser\My (matches the manifest Publisher). Create one once with:
        New-SelfSignedCertificate -Type Custom -Subject "CN=dg" -KeyUsage DigitalSignature `
            -FriendlyName "GoTweaks dev" -CertStoreLocation "Cert:\CurrentUser\My" `
            -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3","2.5.29.19={text}")
#>

[CmdletBinding()]
param(
    [string]$Thumbprint,
    [string]$Configuration = 'Release',
    [switch]$SkipBuild,
    [switch]$NoZip
)

$ErrorActionPreference = 'Stop'
$repo = $PSScriptRoot

function Write-Step($m) { Write-Host "==> $m" -ForegroundColor Cyan }
function Fail($m) { Write-Host "ERROR: $m" -ForegroundColor Red; exit 1 }

# ── Resolve signing certificate ─────────────────────────────────
if (-not $Thumbprint) {
    $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq 'CN=dg' } |
            Sort-Object NotAfter | Select-Object -Last 1
    if (-not $cert) {
        Fail "No 'CN=dg' certificate found in Cert:\CurrentUser\My. Create one (see the header of this script) or pass -Thumbprint."
    }
    $Thumbprint = $cert.Thumbprint
}
Write-Host "Signing thumbprint: $Thumbprint"

# ── Build the signed bundle ─────────────────────────────────────
if (-not $SkipBuild) {
    # vswhere.exe always installs under the x86 Program Files; probe both, tolerate absence.
    $msbuild = $null
    $vswhere = @("${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe",
                 "${env:ProgramFiles}\Microsoft Visual Studio\Installer\vswhere.exe") |
               Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($vswhere) {
        $msbuild = & $vswhere -latest -prerelease -find MSBuild\**\Bin\MSBuild.exe 2>$null |
                   Select-Object -First 1
    }
    if (-not $msbuild) {
        # Fall back to a direct VS install probe, then PATH.
        $msbuild = Get-ChildItem "${env:ProgramFiles}\Microsoft Visual Studio\*\*\MSBuild\Current\Bin\MSBuild.exe" `
                       -ErrorAction SilentlyContinue | Sort-Object FullName | Select-Object -Last 1 -ExpandProperty FullName
    }
    if (-not $msbuild) { $msbuild = 'MSBuild.exe' }   # final fall back to PATH
    Write-Host "MSBuild: $msbuild"

    Write-Step "Building signed MSIX bundle ($Configuration, x64)"
    & $msbuild "$repo\XboxGamingBarPackage\XboxGamingBarPackage.wapproj" `
        /t:Build /p:Configuration=$Configuration /p:Platform=x64 `
        /p:AppxBundlePlatforms=x64 /p:UapAppxPackageBuildMode=SideloadOnly /p:AppxBundle=Always `
        /p:PackageCertificateThumbprint=$Thumbprint /p:PackageCertificateKeyFile= `
        /nologo /v:minimal
    # NOTE: the wapproj build often exits 1 at mspdbcmf.exe (PDB merge, code 1106) while
    # still producing the .msixbundle + .cer. We verify the artifacts below instead of
    # trusting the exit code.
}

# ── Locate the freshest bundle + cer ────────────────────────────
$appPkgs = "$repo\XboxGamingBarPackage\AppPackages"
if (-not (Test-Path $appPkgs)) { Fail "AppPackages folder not found — did the build run?" }

$bundle = Get-ChildItem $appPkgs -Recurse -Filter *.msixbundle |
          Sort-Object LastWriteTime | Select-Object -Last 1
if (-not $bundle) { Fail "No .msixbundle produced under AppPackages." }
$cer = Get-ChildItem $bundle.DirectoryName -Filter *.cer | Select-Object -First 1
if (-not $cer) { Fail "No .cer next to the bundle ($($bundle.DirectoryName))." }

# Derive a friendly version string from the bundle name (…_<ver>_x64.msixbundle).
$version = if ($bundle.Name -match '_(\d+\.\d+\.\d+\.\d+)_') { $Matches[1] } else { 'latest' }
Write-Host "Bundle:  $($bundle.Name)  (version $version)"

# ── Assemble dist/ ──────────────────────────────────────────────
$dist = "$repo\dist"
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Path $dist | Out-Null

$bundleOut = "GoTweaks_$version.msixbundle"
$cerOut    = "GoTweaks_$version.cer"
Copy-Item $bundle.FullName (Join-Path $dist $bundleOut)
Copy-Item $cer.FullName    (Join-Path $dist $cerOut)
Copy-Item "$repo\Installer\Install GoTweaks.ps1" $dist
Copy-Item "$repo\Installer\Install GoTweaks.bat" $dist
Copy-Item "$repo\Installer\README.md"            $dist

Write-Step "Staged release in dist/"
Get-ChildItem $dist | Select-Object Name, @{n='Size';e={'{0:N0} KB' -f ($_.Length/1KB)}} | Format-Table -AutoSize

# ── Zip it for upload ───────────────────────────────────────────
if (-not $NoZip) {
    $zip = Join-Path $dist "GoTweaks_$version.zip"
    Compress-Archive -Path (Join-Path $dist '*') -DestinationPath $zip -Force
    Write-Host "Zip: $zip" -ForegroundColor Green
}

Write-Host ""
Write-Host "Release assembled. Upload the contents of dist/ (or the .zip) to a GitHub Release." -ForegroundColor Green
