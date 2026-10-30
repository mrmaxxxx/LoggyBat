# Builds the two download kits from MSBuild's AppPackages output. Nothing is rebuilt or re-signed:
#   update\          the signed x64 MSIX only (PC that already trusts CN=Max and has the dependencies)
#   first-install\   MSIX + LoggyBat.cer next to it + Microsoft's Install.ps1 / Add-AppDevPackage.ps1
#                    + Dependencies\x64 and \x86 (Add-AppDevPackage installs both on x64)
# Not copied: Dependencies\arm64 (this MSIX is x64-only), Dependencies\win32 (byte copy of x86, never read
# by the scripts), TelemetryDependencies (optional: the scripts skip it when absent; it only sends
# Visual Studio telemetry), *.appxsym (debug symbols).
param(
    [Parameter(Mandatory)][string]$AppPackages,
    [Parameter(Mandatory)][string]$Out
)

$ErrorActionPreference = 'Stop'

$msix = @(Get-ChildItem $AppPackages -Recurse -File -Filter *.msix |
    Where-Object FullName -notmatch '[\\/]Dependencies[\\/]')
if ($msix.Count -ne 1) { throw "Expected exactly one app MSIX in $AppPackages, found $($msix.Count)." }
$msix = $msix[0]
$pkgDir = $msix.Directory.FullName

$cer = Join-Path $AppPackages 'LoggyBat.cer'
if (-not (Test-Path $cer)) { throw "LoggyBat.cer not found in $AppPackages." }

$update = Join-Path $Out 'update'
$first = Join-Path $Out 'first-install'
New-Item -ItemType Directory -Force $update, (Join-Path $first 'Dependencies') | Out-Null
# Long canonical paths, so relative names in the size report below are cut correctly.
$update = (Get-Item $update).FullName
$first = (Get-Item $first).FullName

Copy-Item $msix.FullName $update

# Add-AppDevPackage.ps1 looks for exactly one *.cer and one *.msix in its own folder.
Copy-Item $msix.FullName, $cer, (Join-Path $pkgDir 'Install.ps1'), (Join-Path $pkgDir 'Add-AppDevPackage.ps1') $first
Copy-Item (Join-Path $pkgDir 'Add-AppDevPackage.resources') $first -Recurse
foreach ($arch in 'x64', 'x86') {
    $src = Join-Path $pkgDir "Dependencies\$arch"
    if (-not (Test-Path $src)) { throw "Dependencies\$arch not found in $pkgDir." }
    Copy-Item $src (Join-Path $first 'Dependencies') -Recurse
}

# Self-check of the first-install layout against what the Microsoft scripts expect.
function Count($pattern) { @(Get-ChildItem (Join-Path $first $pattern) -File).Count }
if ((Count '*.cer') -ne 1) { throw 'first-install: expected exactly one .cer next to the MSIX.' }
if ((Count '*.msix') -ne 1 -or (Count '*.appx') -ne 0 -or (Count '*.msixbundle') -ne 0) { throw 'first-install: expected exactly one package.' }
if (-not (Test-Path (Join-Path $first 'Add-AppDevPackage.ps1'))) { throw 'first-install: Add-AppDevPackage.ps1 missing.' }
if (Test-Path (Join-Path $first 'TelemetryDependencies')) { throw 'first-install: TelemetryDependencies must not be included.' }
$archs = @(Get-ChildItem (Join-Path $first 'Dependencies') -Directory | ForEach-Object Name | Sort-Object)
if (($archs -join ',') -ne 'x64,x86') { throw "first-install: Dependencies must hold x64 and x86 only, found: $($archs -join ',')." }
if (@(Get-ChildItem $first -Recurse -File -Include *.appxsym).Count -ne 0) { throw 'first-install: symbols must not be included.' }

# The MSIX in both kits must be the signed package of this build, byte for byte.
$sourceHash = (Get-FileHash $msix.FullName -Algorithm SHA256).Hash
foreach ($kit in $update, $first) {
    $copyHash = (Get-FileHash (Join-Path $kit $msix.Name) -Algorithm SHA256).Hash
    if ($copyHash -ne $sourceHash) { throw "MSIX in $kit differs from the signed package." }
}
"MSIX SHA256 (source = update = first-install): $sourceHash"

# Sizes: every file, then the real size of each kit after zipping (not the sum of the files).
function MB($bytes) { '{0,8:N2} MB' -f ($bytes / 1MB) }
"Source MSIX: $(MB $msix.Length)  $($msix.Name)"
foreach ($kit in $update, $first) {
    $name = Split-Path $kit -Leaf
    "--- $name"
    Get-ChildItem $kit -Recurse -File | ForEach-Object { "$(MB $_.Length)  $($_.FullName.Substring($kit.Length + 1))" }
    $zip = Join-Path ([IO.Path]::GetTempPath()) "loggybat-$name.zip"
    Compress-Archive -Path (Join-Path $kit '*') -DestinationPath $zip -Force
    "$(MB (Get-Item $zip).Length)  = $name zipped (Compress-Archive; the artifact upload logs its own final size)"
    Remove-Item $zip
}
