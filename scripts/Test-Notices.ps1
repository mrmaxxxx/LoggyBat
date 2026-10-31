# Checks that the built MSIX really carries the license/notice texts, not just an index entry:
#  1. app scope matches deps.json both ways: every shipped library (packages and runtime packs) has an
#     entry, and there is no entry for a library that is not shipped;
#  2. every file listed in Notices\notices.json is inside the MSIX with the recorded SHA256;
#  3. every license/notice file present in the resolved NuGet package itself is included (mandatory);
#  4. LoggyBat's own LICENSE and TERMS.md and the template notice THIRD_PARTY_NOTICES.md are inside the
#     MSIX byte for byte equal to the committed file (git HEAD) and to the build checkout.
# Any failure stops the build. Entries that are not plain copies are listed separately as exceptions:
# "unresolved" ones are open; "official-text" and "composed" are resolved with a caveat to review.
param(
    [Parameter(Mandatory)][string]$Msix,
    [Parameter(Mandatory)][string]$Assets,
    [string]$RepoRoot = (Join-Path $PSScriptRoot '..')
)

$ErrorActionPreference = 'Stop'
$noticeName = '^(licen[sc]e|notice|third-party-notices|thirdpartynotices|copying)(\.[a-z]+)?$'
$root = @((Get-Content $Assets -Raw | ConvertFrom-Json).packageFolders.PSObject.Properties.Name)[0]
$own = [ordered]@{
    'Notices/LoggyBat/LICENSE'         = 'LICENSE'
    'Notices/LoggyBat/TERMS.md'        = 'TERMS.md'
    'Notices/THIRD_PARTY_NOTICES.md'   = 'THIRD_PARTY_NOTICES.md'
}
function Hex($bytes) { -join ([Security.Cryptography.SHA256]::Create().ComputeHash($bytes) | ForEach-Object { $_.ToString('x2') }) }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$errors = New-Object System.Collections.Generic.List[string]
$zip = [IO.Compression.ZipFile]::OpenRead($Msix)
try {
    $byName = @{}
    foreach ($e in $zip.Entries) { $byName[[Uri]::UnescapeDataString($e.FullName)] = $e }
    function Read-Bytes([string]$name) {
        $ms = New-Object IO.MemoryStream; $s = $byName[$name].Open()
        try { $s.CopyTo($ms); $ms.ToArray() } finally { $s.Dispose() }
    }
    function Read-Text([string]$name) {
        if (-not $byName.ContainsKey($name)) { throw "MSIX has no $name." }
        [Text.Encoding]::UTF8.GetString((Read-Bytes $name)).TrimStart([char]0xFEFF)
    }

    # ForEach-Object unrolls the array (Windows PowerShell 5.1 returns a JSON array as one object).
    $notices = @((Read-Text 'Notices/notices.json') | ConvertFrom-Json | ForEach-Object { $_ })
    $deps = (Read-Text ($byName.Keys | Where-Object { $_ -like '*.deps.json' } | Select-Object -First 1)) | ConvertFrom-Json

    # 1. both ways
    $shipped = @($deps.libraries.PSObject.Properties | Where-Object { $_.Value.type -in 'package', 'runtimepack' } |
        ForEach-Object { $_.Name -replace '^runtimepack\.', '' })
    $listed = @($notices | ForEach-Object { "$($_.id)/$($_.version)" })
    foreach ($lib in $shipped) { if ($lib -notin $listed) { $errors.Add("no notices entry for shipped $lib") } }
    foreach ($lib in $listed) { if ($lib -notin $shipped) { $errors.Add("notices entry for a library not in deps.json: $lib") } }
    foreach ($n in $notices) { if ($n.scope -ne 'app') { $errors.Add("non-app entry inside the MSIX: $($n.id) $($n.version) ($($n.scope))") } }

    foreach ($n in $notices) {
        # 2. listed files are really inside, unchanged
        foreach ($f in @($n.files)) {
            $path = "Notices/$($n.id)/$($n.version)/$($f.name)"
            if (-not $byName.ContainsKey($path)) { $errors.Add("missing in MSIX: $path"); continue }
            if ((Hex (Read-Bytes $path)) -ne $f.sha256) { $errors.Add("content differs: $path") }
        }
        # 3. nothing the package itself carries was left out
        $dir = Join-Path $root (Join-Path $n.id.ToLowerInvariant() $n.version)
        if (-not (Test-Path $dir)) { $errors.Add("package not in the NuGet cache: $($n.id) $($n.version)"); continue }
        foreach ($pkgFile in @(Get-ChildItem $dir -File | Where-Object { $_.Name -match $noticeName })) {
            if (-not (@($n.files) | Where-Object { $_.name -eq $pkgFile.Name -and $_.origin -eq 'package' })) {
                $errors.Add("mandatory file not included: $($n.id) $($n.version) $($pkgFile.Name)")
            }
        }
    }

    # 4. own texts, byte for byte against git HEAD and the checkout
    foreach ($path in $own.Keys) {
        if (-not $byName.ContainsKey($path)) { $errors.Add("missing in MSIX: $path"); continue }
        $inside = Hex (Read-Bytes $path)
        $tmp = [IO.Path]::GetTempFileName()
        try {
            # cmd redirection keeps the bytes; PowerShell redirection would re-encode them.
            & cmd /c "git -C `"$RepoRoot`" show HEAD:$($own[$path]) > `"$tmp`""
            if ($LASTEXITCODE -ne 0) { throw "git show HEAD:$($own[$path]) failed." }
            $committed = Hex ([IO.File]::ReadAllBytes($tmp))
        } finally { Remove-Item $tmp -ErrorAction SilentlyContinue }
        $checkout = Hex ([IO.File]::ReadAllBytes((Join-Path $RepoRoot $own[$path])))
        if ($inside -ne $committed) { $errors.Add("$path differs from git HEAD:$($own[$path])") }
        if ($inside -ne $checkout) { $errors.Add("$path differs from the checkout file $($own[$path])") }
    }
} finally {
    $zip.Dispose()
}

"Notices in MSIX: $($notices.Count) app packages = $($shipped.Count) libraries in deps.json; $(@($notices | ForEach-Object { @($_.files) }).Count) files."
foreach ($n in $notices | Where-Object status -ne 'copied') {
    $line = "$($n.status): $($n.id) $($n.version) - $($n.note)"
    "Exception $line"
    if ($env:GITHUB_ACTIONS) {
        $level = if ($n.status -eq 'unresolved') { 'warning' } else { 'notice' }
        "::$level title=License exception::$line"
    }
}
if ($errors.Count) {
    if ($env:GITHUB_ACTIONS) { $errors | ForEach-Object { "::error title=Notices check::$_" } }
    throw "Notices check failed:`n  $($errors -join "`n  ")"
}
"Notices check passed; LICENSE, TERMS.md and THIRD_PARTY_NOTICES.md inside the MSIX equal git HEAD and the checkout."
if ($env:GITHUB_ACTIONS) { "::notice title=Notices::app scope = deps.json ($($shipped.Count) libraries), all texts inside the MSIX with matching SHA256; own texts equal git HEAD" }
