# Builds the two download kits from MSBuild's AppPackages output. Nothing is rebuilt or re-signed:
#   update\          the signed x64 MSIX only (PC that already trusts CN=Max and has the dependencies);
#                    its license/notice texts are inside the MSIX (Notices\, see LoggyBat.Package.wapproj)
#   first-install\   MSIX + LoggyBat.cer next to it + Install-LoggyBat.ps1 (prerequisite check, then
#                    Microsoft's Install.ps1 / Add-AppDevPackage.ps1) + Dependencies\x64 and \x86 with the
#                    Windows App Runtime packages only + Notices\ (from -NoticeSources)
# Not copied: VCLibs (WidBar, the required first step, gets the same VCLibs from Microsoft Store;
# Install-LoggyBat.ps1 checks them), Dependencies\arm64 (this MSIX is x64-only), Dependencies\win32
# (byte copy of x86, never read by the scripts), TelemetryDependencies (optional for the scripts; it only
# sends Visual Studio telemetry), *.appxsym (debug symbols).
param(
    [Parameter(Mandatory)][string]$AppPackages,
    [Parameter(Mandatory)][string]$Out,
    [string[]]$NoticeSources = @()
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
Copy-Item (Join-Path $PSScriptRoot 'Install-LoggyBat.ps1') $first
foreach ($arch in 'x64', 'x86') {
    $src = Join-Path $pkgDir "Dependencies\$arch"
    if (-not (Test-Path $src)) { throw "Dependencies\$arch not found in $pkgDir." }
    $dst = New-Item -ItemType Directory -Force (Join-Path $first "Dependencies\$arch")
    Get-ChildItem $src -File | Where-Object Name -notlike 'Microsoft.VCLibs.*' | Copy-Item -Destination $dst
}
if ($NoticeSources) {
    New-Item -ItemType Directory -Force (Join-Path $first 'Notices') | Out-Null
    foreach ($src in $NoticeSources) {
        $from = if (Test-Path $src -PathType Container) { Join-Path $src '*' } else { $src }
        Copy-Item $from (Join-Path $first 'Notices') -Recurse -ErrorAction Stop
    }
}

# Self-check of the first-install layout against what the Microsoft scripts expect.
function Count($pattern) { @(Get-ChildItem (Join-Path $first $pattern) -File).Count }
if ((Count '*.cer') -ne 1) { throw 'first-install: expected exactly one .cer next to the MSIX.' }
if ((Count '*.msix') -ne 1 -or (Count '*.appx') -ne 0 -or (Count '*.msixbundle') -ne 0) { throw 'first-install: expected exactly one package.' }
foreach ($script in 'Add-AppDevPackage.ps1', 'Install.ps1', 'Install-LoggyBat.ps1') {
    if (-not (Test-Path (Join-Path $first $script))) { throw "first-install: $script missing." }
}
if (Test-Path (Join-Path $first 'TelemetryDependencies')) { throw 'first-install: TelemetryDependencies must not be included.' }
$archs = @(Get-ChildItem (Join-Path $first 'Dependencies') -Directory | ForEach-Object Name | Sort-Object)
if (($archs -join ',') -ne 'x64,x86') { throw "first-install: Dependencies must hold x64 and x86 only, found: $($archs -join ',')." }
if (@(Get-ChildItem $first -Recurse -File -Include *.appxsym).Count -ne 0) { throw 'first-install: symbols must not be included.' }
$vclibsInKit = @(Get-ChildItem $first -Recurse -File -Filter 'Microsoft.VCLibs.*')
if ($vclibsInKit) { throw "first-install: VCLibs must not be included, found: $($vclibsInKit.Name -join ', ')." }
foreach ($need in 'INDEX.txt', 'notices.json', 'THIRD_PARTY_NOTICES.md') {
    if ($NoticeSources -and -not (Test-Path (Join-Path $first "Notices\$need"))) { throw "first-install: Notices\$need missing." }
}

# What the MSIX really requires vs. what first-install brings, read from the package manifests
# (not assumed from version numbers). VCLibs come with WidBar from Microsoft Store and are checked on the
# user's PC by Install-LoggyBat.ps1; every other x64 requirement must be met by a bundled x64 package.
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Read-Manifest([string]$path) {
    $zip = [IO.Compression.ZipFile]::OpenRead($path)
    try {
        $reader = New-Object IO.StreamReader($zip.GetEntry('AppxManifest.xml').Open())
        try { [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
    } finally { $zip.Dispose() }
}
$manifest = Read-Manifest $msix.FullName
if (-not $manifest.Package.Dependencies.SelectNodes('*').Count) { throw 'MSIX manifest lists no dependencies.' }
$required = @($manifest.Package.Dependencies.PackageDependency)
$bundled = @(Get-ChildItem (Join-Path $first 'Dependencies') -Recurse -File | ForEach-Object {
    $id = (Read-Manifest $_.FullName).Package.Identity
    [pscustomobject]@{ Name = $id.Name; Version = [version]$id.Version; Arch = $id.ProcessorArchitecture; File = $_.Name }
})
'MSIX requires:'
$required | ForEach-Object { "  $($_.Name) >= $($_.MinVersion)  ($($_.Publisher))" }
'first-install Dependencies bring:'
$bundled | ForEach-Object { "  $($_.Name) $($_.Version) $($_.Arch)  ($($_.File))" }
foreach ($dep in $required) {
    if ($dep.Name -like 'Microsoft.VCLibs.*') {
        $text = "MSIX requires $($dep.Name) >= $($dep.MinVersion); not bundled - comes with WidBar from Microsoft Store, checked by Install-LoggyBat.ps1"
    } else {
        $match = @($bundled | Where-Object { $_.Name -eq $dep.Name -and $_.Arch -eq 'x64' -and $_.Version -ge [version]$dep.MinVersion })
        if (-not $match) { throw "first-install: no x64 $($dep.Name) >= $($dep.MinVersion) in Dependencies." }
        $text = "MSIX requires $($dep.Name) >= $($dep.MinVersion); first-install has x64 $($match[0].Version)"
    }
    $text
    # Also as a GitHub annotation: readable without signing in, unlike the log.
    if ($env:GITHUB_ACTIONS) { "::notice title=Dependency::$text" }
}

# Where the VCLibs that MSBuild put into AppPackages come from on this machine; log only.
$sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Microsoft SDKs\Windows Kits\10\ExtensionSDKs'
if (Test-Path $sdkRoot) {
    foreach ($vclib in Get-ChildItem (Join-Path $pkgDir 'Dependencies') -Recurse -Filter 'Microsoft.VCLibs.*') {
        $hash = (Get-FileHash $vclib.FullName).Hash
        $sources = @(Get-ChildItem $sdkRoot -Recurse -File -Filter $vclib.Name -ErrorAction SilentlyContinue |
            Where-Object { (Get-FileHash $_.FullName).Hash -eq $hash } | ForEach-Object FullName)
        $text = "$($vclib.Directory.Name)\$($vclib.Name) = $(if ($sources) { $sources -join ', ' } else { 'no identical file under ExtensionSDKs' })"
        "VCLibs source (not shipped): $text"
        if ($env:GITHUB_ACTIONS) { "::notice title=VCLibs source::$text" }
    }
}

# The MSIX in both kits must be the signed package of this build, byte for byte.
$sourceHash = (Get-FileHash $msix.FullName -Algorithm SHA256).Hash
foreach ($kit in $update, $first) {
    $copyHash = (Get-FileHash (Join-Path $kit $msix.Name) -Algorithm SHA256).Hash
    if ($copyHash -ne $sourceHash) { throw "MSIX in $kit differs from the signed package." }
}
"MSIX SHA256 (source = update = first-install): $sourceHash"
if ($env:GITHUB_ACTIONS) {
    $noticeFiles = @(Get-ChildItem (Join-Path $first 'Notices') -Recurse -File -ErrorAction SilentlyContinue).Count
    "::notice title=Kits::update = MSIX (notices inside); first-install = MSIX + $noticeFiles files in Notices\, VCLibs files: $($vclibsInKit.Count)"
}

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
