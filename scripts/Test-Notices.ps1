# Checks that the built MSIX really carries the license/notice texts, not just an index entry:
#  1. every library in the MSIX deps.json (packages and runtime packs) has an entry in Notices\notices.json;
#  2. every file listed there is inside the MSIX under Notices\<id>\<version>\ with the recorded SHA256;
#  3. every license/notice file present in the resolved NuGet package itself is among them (mandatory);
#  4. THIRD_PARTY_NOTICES.md (template notice) is inside the MSIX.
# Any failure stops the build. Entries that are not plain copies are listed separately as exceptions:
# "unresolved" ones are open; "official-text" and "composed" are resolved with a caveat to review.
param(
    [Parameter(Mandatory)][string]$Msix,
    [Parameter(Mandatory)][string]$Assets
)

$ErrorActionPreference = 'Stop'
$noticeName = '^(licen[sc]e|notice|third-party-notices|thirdpartynotices|copying)(\.[a-z]+)?$'
$root = @((Get-Content $Assets -Raw | ConvertFrom-Json).packageFolders.PSObject.Properties.Name)[0]

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($Msix)
try {
    $byName = @{}
    foreach ($e in $zip.Entries) { $byName[[Uri]::UnescapeDataString($e.FullName)] = $e }
    function Read-Text([string]$name) {
        if (-not $byName.ContainsKey($name)) { throw "MSIX has no $name." }
        $reader = New-Object IO.StreamReader($byName[$name].Open())
        try { $reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    function Hash-Entry([string]$name) {
        $stream = $byName[$name].Open()
        try { -join ([Security.Cryptography.SHA256]::Create().ComputeHash($stream) | ForEach-Object { $_.ToString('x2') }) }
        finally { $stream.Dispose() }
    }

    # ForEach-Object unrolls the array (Windows PowerShell 5.1 returns a JSON array as one object).
    $notices = @((Read-Text 'Notices/notices.json') | ConvertFrom-Json | ForEach-Object { $_ })
    $deps = (Read-Text ($byName.Keys | Where-Object { $_ -like '*.deps.json' } | Select-Object -First 1)) | ConvertFrom-Json
    $errors = New-Object System.Collections.Generic.List[string]

    # 1. coverage
    $shipped = $deps.libraries.PSObject.Properties | Where-Object { $_.Value.type -in 'package', 'runtimepack' } |
        ForEach-Object { $_.Name -replace '^runtimepack\.', '' }
    foreach ($lib in $shipped) {
        $id, $version = $lib.Split('/')
        if (-not ($notices | Where-Object { $_.id -eq $id -and $_.version -eq $version })) { $errors.Add("no notices entry for $id $version") }
    }

    foreach ($n in $notices) {
        # 2. listed files are really inside, unchanged
        foreach ($f in @($n.files)) {
            $path = "Notices/$($n.id)/$($n.version)/$($f.name)"
            if (-not $byName.ContainsKey($path)) { $errors.Add("missing in MSIX: $path"); continue }
            if ((Hash-Entry $path) -ne $f.sha256) { $errors.Add("content differs: $path") }
        }
        # 3. nothing the package itself carries was left out
        $dir = Join-Path $root (Join-Path $n.id.ToLowerInvariant() $n.version)
        foreach ($own in @(Get-ChildItem $dir -File | Where-Object { $_.Name -match $noticeName })) {
            if (-not (@($n.files) | Where-Object { $_.name -eq $own.Name -and $_.origin -eq 'package' })) {
                $errors.Add("mandatory file not included: $($n.id) $($n.version) $($own.Name)")
            }
        }
    }

    # 4. template notice
    if (-not $byName.ContainsKey('Notices/THIRD_PARTY_NOTICES.md')) { $errors.Add('missing in MSIX: Notices/THIRD_PARTY_NOTICES.md') }
} finally {
    $zip.Dispose()
}

$copied = @($notices | Where-Object status -eq 'copied')
"Notices in MSIX: $(@($notices).Count) packages, $(@($notices | ForEach-Object { @($_.files) }).Count) files; $($copied.Count) packages with their own files copied unchanged."
foreach ($n in $notices | Where-Object status -ne 'copied') {
    $line = "$($n.status): $($n.id) $($n.version) - $($n.note)"
    "Exception $line"
    if ($env:GITHUB_ACTIONS) {
        $level = if ($n.status -eq 'unresolved') { 'warning' } else { 'notice' }
        "::$level title=License exception::$line"
    }
}
if ($errors.Count) { throw "Notices check failed:`n  $($errors -join "`n  ")" }
"Notices check passed."
if ($env:GITHUB_ACTIONS) { "::notice title=Notices::$(@($notices).Count) packages, all listed texts inside the MSIX with matching SHA256" }
