# Removes any "Windows/<ver>" reference-assembly (Windows.winmd) entry from a
# generated .deps.json. A .winmd listed as a runtime asset is placed in the
# CoreCLR TPA, which makes the runtime fail to start ("Failed to create CoreCLR,
# HRESULT: 0x80070057"). winmd is compile-time-only metadata; the tests never
# resolve a WinRT type at runtime, so dropping it from deps.json is safe.
param([Parameter(Mandatory = $true)][string]$DepsPath)

if (-not (Test-Path -LiteralPath $DepsPath)) { exit 0 }

try {
    $json = Get-Content -LiteralPath $DepsPath -Raw | ConvertFrom-Json
    $removed = $false

    if ($json.targets) {
        foreach ($target in @($json.targets.PSObject.Properties)) {
            foreach ($lib in @($target.Value.PSObject.Properties)) {
                if ($lib.Name -like 'Windows/*') {
                    $target.Value.PSObject.Properties.Remove($lib.Name)
                    $removed = $true
                }
            }
        }
    }

    if ($json.libraries) {
        foreach ($lib in @($json.libraries.PSObject.Properties)) {
            if ($lib.Name -like 'Windows/*') {
                $json.libraries.PSObject.Properties.Remove($lib.Name)
                $removed = $true
            }
        }
    }

    if ($removed) {
        $json | ConvertTo-Json -Depth 64 | Set-Content -LiteralPath $DepsPath -Encoding utf8
        Write-Host "strip-winmd-deps: removed Windows.winmd entries from $DepsPath"
    }
}
catch {
    Write-Host "strip-winmd-deps: WARNING - $($_.Exception.Message)"
}

exit 0
