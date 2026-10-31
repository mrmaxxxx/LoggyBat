# Builds the two download kits from MSBuild's AppPackages output. Nothing is rebuilt or re-signed.
# Both kits carry, readable before installing: TERMS.md, LICENSE (LoggyBat's own code), Notices\ (third-party
# licenses/notices of the components inside the MSIX + THIRD_PARTY_NOTICES.md) and the script that shows the
# terms and asks for consent before changing anything (LoggyBat-Terms.ps1).
#   update\          the signed x64 MSIX + Update-LoggyBat.ps1 (for an installed LoggyBat only)
#   first-install\   MSIX + LoggyBat.cer next to it + Install-LoggyBat.ps1 (terms, prerequisite check, then
#                    Microsoft's Install.ps1 / Add-AppDevPackage.ps1) + Dependencies\x64 and \x86 with the
#                    Windows App Runtime packages + Notices\kit\ (notices of those packages)
# Not copied: VCLibs (WidBar, the required first step, gets the same VCLibs from Microsoft Store;
# Install-LoggyBat.ps1 checks them), Dependencies\arm64 (this MSIX is x64-only), Dependencies\win32
# (byte copy of x86, never read by the scripts), TelemetryDependencies (optional for the scripts; it only
# sends Visual Studio telemetry), *.appxsym (debug symbols).
param(
    [Parameter(Mandatory)][string]$AppPackages,
    [Parameter(Mandatory)][string]$Out,
    [Parameter(Mandatory)][string]$AppNotices,
    [Parameter(Mandatory)][string]$KitNotices,
    [string]$RepoRoot = (Join-Path $PSScriptRoot '..')
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

# Terms, own license and notices: the same in both kits.
foreach ($kit in $update, $first) {
    Copy-Item $msix.FullName, (Join-Path $RepoRoot 'TERMS.md'), (Join-Path $RepoRoot 'LICENSE'),
        (Join-Path $PSScriptRoot 'LoggyBat-Terms.ps1') $kit
    $notices = New-Item -ItemType Directory -Force (Join-Path $kit 'Notices')
    Copy-Item (Join-Path $AppNotices '*') $notices -Recurse
    Copy-Item (Join-Path $RepoRoot 'THIRD_PARTY_NOTICES.md') $notices
}
Copy-Item (Join-Path $PSScriptRoot 'Update-LoggyBat.ps1') $update

# Add-AppDevPackage.ps1 looks for exactly one *.cer and one *.msix in its own folder.
Copy-Item $cer, (Join-Path $pkgDir 'Install.ps1'), (Join-Path $pkgDir 'Add-AppDevPackage.ps1'),
    (Join-Path $PSScriptRoot 'Install-LoggyBat.ps1') $first
Copy-Item (Join-Path $pkgDir 'Add-AppDevPackage.resources') $first -Recurse
foreach ($arch in 'x64', 'x86') {
    $src = Join-Path $pkgDir "Dependencies\$arch"
    if (-not (Test-Path $src)) { throw "Dependencies\$arch not found in $pkgDir." }
    $dst = New-Item -ItemType Directory -Force (Join-Path $first "Dependencies\$arch")
    Get-ChildItem $src -File | Where-Object Name -notlike 'Microsoft.VCLibs.*' | Copy-Item -Destination $dst
}
$kitNoticesDst = New-Item -ItemType Directory -Force (Join-Path $first 'Notices\kit')
Copy-Item (Join-Path $KitNotices '*') $kitNoticesDst -Recurse

# Layout checks.
function Count($dir, $pattern) { @(Get-ChildItem (Join-Path $dir $pattern) -File).Count }
foreach ($kit in $update, $first) {
    $name = Split-Path $kit -Leaf
    if ((Count $kit '*.msix') -ne 1 -or (Count $kit '*.appx') -ne 0 -or (Count $kit '*.msixbundle') -ne 0) { throw "${name}: expected exactly one package." }
    foreach ($need in 'TERMS.md', 'LICENSE', 'LoggyBat-Terms.ps1', 'Notices\INDEX.txt', 'Notices\notices.json', 'Notices\THIRD_PARTY_NOTICES.md') {
        if (-not (Test-Path (Join-Path $kit $need))) { throw "${name}: $need missing." }
    }
    foreach ($same in 'TERMS.md', 'LICENSE') {
        if ((Get-FileHash (Join-Path $kit $same)).Hash -ne (Get-FileHash (Join-Path $RepoRoot $same)).Hash) { throw "${name}: $same differs from the repository file." }
    }
}
if (-not (Test-Path (Join-Path $update 'Update-LoggyBat.ps1'))) { throw 'update: Update-LoggyBat.ps1 missing.' }
if ((Count $first '*.cer') -ne 1) { throw 'first-install: expected exactly one .cer next to the MSIX.' }
foreach ($script in 'Add-AppDevPackage.ps1', 'Install.ps1', 'Install-LoggyBat.ps1', 'Notices\kit\INDEX.txt', 'Notices\kit\notices.json') {
    if (-not (Test-Path (Join-Path $first $script))) { throw "first-install: $script missing." }
}
if (Test-Path (Join-Path $first 'TelemetryDependencies')) { throw 'first-install: TelemetryDependencies must not be included.' }
$archs = @(Get-ChildItem (Join-Path $first 'Dependencies') -Directory | ForEach-Object Name | Sort-Object)
if (($archs -join ',') -ne 'x64,x86') { throw "first-install: Dependencies must hold x64 and x86 only, found: $($archs -join ',')." }
if (@(Get-ChildItem $first -Recurse -File -Include *.appxsym).Count -ne 0) { throw 'first-install: symbols must not be included.' }
$vclibsInKit = @(Get-ChildItem $Out -Recurse -File -Filter 'Microsoft.VCLibs.*')
if ($vclibsInKit) { throw "VCLibs must not be included, found: $($vclibsInKit.Name -join ', ')." }

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
$required = @((Read-Manifest $msix.FullName).Package.Dependencies.PackageDependency)
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
    if ($env:GITHUB_ACTIONS) { "::notice title=Dependency::$text" }
}

# Every bundled package must be covered by the kit notices: Windows App Runtime packages come from the
# Microsoft.WindowsAppSDK.Runtime NuGet package.
$kitEntries = @((Get-Content (Join-Path $first 'Notices\kit\notices.json') -Raw | ConvertFrom-Json) | ForEach-Object { $_ })
foreach ($b in $bundled) {
    $source = if ($b.Name -like 'Microsoft.WindowsAppRuntime.*') { 'Microsoft.WindowsAppSDK.Runtime' } else { $null }
    if (-not $source) { throw "first-install: no known NuGet source for bundled $($b.Name); add its notices before shipping it." }
    if (-not ($kitEntries | Where-Object id -eq $source)) { throw "first-install: Notices\kit has no entry for $source (source of $($b.File))." }
}
"first-install Notices\kit: $($kitEntries.Count) packages ($(($kitEntries | ForEach-Object { "$($_.id) $($_.version)" }) -join ', '))"

# Where the VCLibs that MSBuild put into AppPackages come from on this machine; log only.
$sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Microsoft SDKs\Windows Kits\10\ExtensionSDKs'
if (Test-Path $sdkRoot) {
    foreach ($vclib in Get-ChildItem (Join-Path $pkgDir 'Dependencies') -Recurse -Filter 'Microsoft.VCLibs.*') {
        $hash = (Get-FileHash $vclib.FullName).Hash
        $sources = @(Get-ChildItem $sdkRoot -Recurse -File -Filter $vclib.Name -ErrorAction SilentlyContinue |
            Where-Object { (Get-FileHash $_.FullName).Hash -eq $hash } | ForEach-Object FullName)
        "VCLibs source (not shipped): $($vclib.Directory.Name)\$($vclib.Name) = $(if ($sources) { $sources -join ', ' } else { 'no identical file under ExtensionSDKs' })"
    }
}

# The MSIX in both kits must be the signed package of this build, byte for byte.
$sourceHash = (Get-FileHash $msix.FullName -Algorithm SHA256).Hash
foreach ($kit in $update, $first) {
    if ((Get-FileHash (Join-Path $kit $msix.Name) -Algorithm SHA256).Hash -ne $sourceHash) { throw "MSIX in $kit differs from the signed package." }
}
"MSIX SHA256 (source = update = first-install): $sourceHash"
if ($env:GITHUB_ACTIONS) {
    $u = @(Get-ChildItem (Join-Path $update 'Notices') -Recurse -File).Count
    $f = @(Get-ChildItem (Join-Path $first 'Notices') -Recurse -File).Count
    "::notice title=Kits::update: MSIX, Update-LoggyBat.ps1, TERMS.md, LICENSE, $u files in Notices\; first-install: MSIX, Install-LoggyBat.ps1, TERMS.md, LICENSE, $f files in Notices\ (incl. kit), VCLibs files: 0"
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
