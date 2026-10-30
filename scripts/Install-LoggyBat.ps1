# LoggyBat - first installation. Run from the unpacked first-install kit:
#   right-click Install-LoggyBat.ps1 > Run with PowerShell
#   or: powershell -ExecutionPolicy Bypass -File .\Install-LoggyBat.ps1 [-CheckOnly]
# 1. Checks, for the current user, the VCLibs framework packages that the LoggyBat manifest requires
#    (name, publisher, architecture, minimum version - all read from the manifest, nothing lowered).
#    They are not in this kit: they come with WidBar from Microsoft Store, which must be installed first.
# 2. Only if they are present, runs Microsoft's Install.ps1 from this folder (certificate, Windows App
#    Runtime from Dependencies\, LoggyBat). -CheckOnly stops after step 1.
# Nothing is downloaded by this script.
param(
    [switch]$CheckOnly,
    [string]$Package   # the LoggyBat .msix; default: the only .msix next to this script
)

$ErrorActionPreference = 'Stop'
$widBarStore = 'https://apps.microsoft.com/detail/9PKLDNM83TP9'

if (-not $Package) {
    $found = @(Get-ChildItem $PSScriptRoot -File -Filter *.msix)
    if ($found.Count -ne 1) { throw "Expected exactly one .msix next to Install-LoggyBat.ps1, found $($found.Count)." }
    $Package = $found[0].FullName
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($Package)
try {
    $reader = New-Object IO.StreamReader($zip.GetEntry('AppxManifest.xml').Open())
    try { $manifest = [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
} finally { $zip.Dispose() }

$arch = $manifest.Package.Identity.ProcessorArchitecture
$required = @($manifest.Package.Dependencies.PackageDependency | Where-Object Name -like 'Microsoft.VCLibs.*')
if (-not $required) { throw 'The LoggyBat manifest names no VCLibs dependency; this check does not match the package.' }

$problems = @()
foreach ($dep in $required) {
    $installed = @(Get-AppxPackage -Name $dep.Name |
        Where-Object { $_.Publisher -eq $dep.Publisher -and "$($_.Architecture)" -eq $arch } |
        Sort-Object { [version]$_.Version } -Descending)
    if (-not $installed) {
        $problems += "$($dep.Name) ($arch): not installed / не установлен"
    } elseif ([version]$installed[0].Version -lt [version]$dep.MinVersion) {
        $problems += "$($dep.Name) ($arch): $($installed[0].Version) < $($dep.MinVersion)"
    } else {
        "OK: $($dep.Name) $($installed[0].Version) $arch >= $($dep.MinVersion)"
    }
}

if ($problems) {
    $problems | ForEach-Object { "MISSING: $_" }
    Write-Host ''
    Write-Host 'LoggyBat не установлен: нужные компоненты Microsoft Visual C++ (VCLibs) отсутствуют или устарели.' -ForegroundColor Yellow
    Write-Host "Они устанавливаются вместе с WidBar из Microsoft Store. Установите или обновите WidBar:"
    Write-Host "  $widBarStore"
    Write-Host 'затем запустите Install-LoggyBat.ps1 снова.'
    Write-Host ''
    Write-Host 'LoggyBat was not installed: the required Microsoft Visual C++ framework packages (VCLibs) are missing or too old.' -ForegroundColor Yellow
    Write-Host "They are installed together with WidBar from Microsoft Store. Install or update WidBar ($widBarStore), then run Install-LoggyBat.ps1 again."
    exit 1
}

if ($CheckOnly) { 'Prerequisites OK / Проверка пройдена'; return }
& (Join-Path $PSScriptRoot 'Install.ps1')
