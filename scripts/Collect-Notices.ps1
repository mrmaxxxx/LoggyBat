# Copies license and third-party notice files, unchanged, from the NuGet packages that NuGet actually
# resolved for LoggyBat (project.assets.json). Runs before the MSIX exists, so the selection is made from
# the same data deps.json is later generated from. Two scopes, each with its own INDEX.txt and notices.json:
#   app (-OutApp) - goes INTO the MSIX: packages with runtime/native files for the RID, or depending on such a package
#                   (what deps.json lists)
#                   and the runtime packs of the frameworks the project references (frameworkReferences);
#   kit (-OutKit) - goes into the first-install kit only: the other Microsoft.WindowsAppSDK* packages; the
#                   kit ships the Windows App Runtime packages built from them (Microsoft.WindowsAppSDK.Runtime).
# Runtime packs the .NET SDK merely downloads (e.g. ASP.NET Core, WindowsDesktop) without a framework
# reference are not shipped anywhere; they are logged as ignored, not collected.
# Status of each package:
#   copied        - the package's own license/notice files, byte for byte;
#   official-text - no file in the package; licenses\<id>\source.json names the official text, stored
#                   unchanged in the repo, for exactly the licenseUrl the package's nuspec states;
#   composed      - no file; SPDX expression MIT + nuspec copyright -> standard MIT text, marked as composed;
#   unresolved    - none of the above; listed as an open exception, never as done.
param(
    [Parameter(Mandatory)][string]$Assets,
    [Parameter(Mandatory)][string]$OutApp,
    [Parameter(Mandatory)][string]$OutKit,
    [string]$Rid = 'win-x64',
    # Framework references the .NET SDK adds implicitly at build time (so project.assets.json does not list
    # them under frameworkReferences): the Windows SDK projection for net*-windows10.0.x target frameworks.
    [string[]]$ImplicitFrameworks = @('Microsoft.Windows.SDK.NET.Ref'),
    [string]$OfficialTexts = (Join-Path $PSScriptRoot '..\licenses')
)

$ErrorActionPreference = 'Stop'
$noticeName = '^(licen[sc]e|notice|third-party-notices|thirdpartynotices|copying)(\.[a-z]+)?$'

$assetsJson = Get-Content $Assets -Raw | ConvertFrom-Json
$root = @($assetsJson.packageFolders.PSObject.Properties.Name)[0]
$target = @($assetsJson.targets.PSObject.Properties | Where-Object Name -like "*/$Rid")[0]
if (-not $target) { throw "No $Rid target in $Assets." }

# deps.json keeps a package if it has runtime files or depends (transitively) on one that does; packages that
# are empty all the way down are trimmed from it. Same rule here, repeated until nothing changes.
$libs = @($target.Value.PSObject.Properties | Where-Object { $_.Value.type -eq 'package' })
$shipped = @{}
do {
    $added = $false
    foreach ($lib in $libs) {
        if ($shipped[$lib.Name]) { continue }
        $deps = @($lib.Value.dependencies.PSObject.Properties.Name)
        $hit = $lib.Value.runtime -or $lib.Value.native -or $lib.Value.runtimeTargets -or $lib.Value.resource -or
               @($libs | Where-Object { $shipped[$_.Name] -and $_.Name.Split('/')[0] -in $deps }).Count
        if ($hit) { $shipped[$lib.Name] = $true; $added = $true }
    }
} while ($added)
$scope = [ordered]@{}
foreach ($lib in $libs) {
    if ($shipped[$lib.Name]) { $scope[$lib.Name] = 'app' }
    elseif ($lib.Name -like 'Microsoft.WindowsAppSDK*') { $scope[$lib.Name] = 'kit' }
}
foreach ($fw in $assetsJson.project.frameworks.PSObject.Properties) {
    $refs = @($fw.Value.frameworkReferences.PSObject.Properties.Name) + $ImplicitFrameworks
    $text = "frameworks ($($fw.Name)): $($refs -join ', ') (implicit: $($ImplicitFrameworks -join ', '))"
    $text; if ($env:GITHUB_ACTIONS) { "::notice title=Notices selection::$text" }
    foreach ($dd in @($fw.Value.downloadDependencies)) {
        $key = "$($dd.name)/$($dd.version.Trim('[]').Split(',')[0].Trim())"
        $referenced = $refs | Where-Object { $dd.name -eq $_ -or $dd.name -eq "$_.Runtime.$Rid" }
        if ($referenced) { $scope[$key] = 'app'; continue }
        $text = "ignored download dependency (not referenced, not shipped): $key"
        $text; if ($env:GITHUB_ACTIONS) { "::notice title=Notices selection::$text" }
    }
}

function Sha256([string]$path) { (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant() }

$entries = @()
foreach ($key in $scope.Keys) {
    $id, $version = $key.Split('/')
    $out = if ($scope[$key] -eq 'app') { $OutApp } else { $OutKit }
    $dir = Join-Path $root (Join-Path $id.ToLowerInvariant() $version)
    if (-not (Test-Path $dir)) { throw "Package folder not found: $dir" }
    $dest = Join-Path $out (Join-Path $id $version)

    $nuspecFile = Get-ChildItem $dir -Filter *.nuspec | Select-Object -First 1
    $meta = if ($nuspecFile) { ([xml](Get-Content $nuspecFile.FullName -Raw)).package.metadata } else { $null }
    $licenseType = "$($meta.license.type)"
    $licenseText = if ($meta.license) { "$($meta.license.'#text')" } else { '' }
    $licenseUrl = "$($meta.licenseUrl)"
    $license = if ($licenseType) { "${licenseType}: $licenseText" } elseif ($licenseUrl) { "url: $licenseUrl" } else { 'not stated' }

    $entry = [ordered]@{ id = $id; version = $version; scope = $scope[$key]; license = $license; status = ''; note = ''; files = @() }
    $files = @(Get-ChildItem $dir -File | Where-Object { $_.Name -match $noticeName })
    $official = Join-Path $OfficialTexts (Join-Path $id 'source.json')

    if ($files) {
        New-Item -ItemType Directory -Force $dest | Out-Null
        foreach ($f in $files) {
            Copy-Item $f.FullName $dest
            $entry.files += [ordered]@{ name = $f.Name; sha256 = (Sha256 $f.FullName); origin = 'package' }
        }
        $entry.status = 'copied'
    } elseif ((Test-Path $official) -and ($src = Get-Content $official -Raw | ConvertFrom-Json) -and
              $src.appliesWhenNuspecLicenseUrl -eq $licenseUrl) {
        $text = Join-Path (Split-Path $official) $src.file
        if ((Sha256 $text) -ne $src.sha256) { throw "$text does not match the SHA256 recorded in $official." }
        New-Item -ItemType Directory -Force $dest | Out-Null
        Copy-Item $text, $official $dest
        $entry.files += [ordered]@{ name = $src.file; sha256 = $src.sha256; origin = 'official-text' }
        $entry.files += [ordered]@{ name = 'source.json'; sha256 = (Sha256 $official); origin = 'source-record' }
        $entry.status = 'official-text'
        $entry.note = "official text for nuspec licenseUrl $licenseUrl, retrieved $($src.retrieved) from $($src.resolvedUrl)"
    } elseif ($licenseType -eq 'expression' -and $licenseText -eq 'MIT' -and $meta.copyright) {
        New-Item -ItemType Directory -Force $dest | Out-Null
        $mit = @"
This file was composed for LoggyBat: the $id $version package states its license only as the SPDX
expression "MIT" with the copyright line below and contains no license file.

MIT License

$($meta.copyright)

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
"@
        $composed = Join-Path $dest 'LICENSE-MIT-composed.txt'
        Set-Content -Path $composed -Value $mit -Encoding utf8
        $entry.files += [ordered]@{ name = 'LICENSE-MIT-composed.txt'; sha256 = (Sha256 $composed); origin = 'composed' }
        $entry.status = 'composed'
        $entry.note = "composed from nuspec: expression MIT, $($meta.copyright)"
    } else {
        $entry.status = 'unresolved'
        $entry.note = 'no license file in the package and no confirmed official text'
    }
    $entries += $entry
}

$headers = @{
    app = 'Third-party components INSIDE the LoggyBat package (MSIX), from the NuGet packages resolved for this build.'
    kit = 'Third-party packages shipped NEXT TO the MSIX in the first-install kit (Windows App Runtime), from the NuGet packages resolved for this build.'
}
foreach ($s in 'app', 'kit') {
    $out = if ($s -eq 'app') { $OutApp } else { $OutKit }
    $mine = @($entries | Where-Object { $_.scope -eq $s })
    New-Item -ItemType Directory -Force $out | Out-Null
    ConvertTo-Json @($mine) -Depth 5 | Set-Content -Path (Join-Path $out 'notices.json') -Encoding utf8
    $index = @(
        $headers[$s]
        'Folder layout: <package id>\<version>\<file>. Status: copied = the package''s own files, unchanged;'
        'official-text = no file in the package, official text named by its nuspec (see source.json);'
        'composed = no file, standard MIT text composed from the nuspec; unresolved = open, needs review.'
        ''
    )
    $index += $mine | ForEach-Object {
        "$($_.id) $($_.version)  [$($_.license)]  $($_.status): $(($_.files | ForEach-Object name) -join ', ')$(if ($_.note) { "  ($($_.note))" })"
    }
    Set-Content -Path (Join-Path $out 'INDEX.txt') -Value $index -Encoding utf8
    "--- $s"
    $index | Select-Object -Skip 5
}
