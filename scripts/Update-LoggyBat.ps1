# LoggyBat - update an installed LoggyBat. Run from the unpacked update kit:
#   right-click Update-LoggyBat.ps1 > Run with PowerShell
#   or: powershell -ExecutionPolicy Bypass -File .\Update-LoggyBat.ps1 [-AcceptTerms]
# Checks first, changes nothing until all pass:
#   - LoggyBat is already installed (otherwise use the first-install kit) and is older than this package;
#   - the package signature is valid and its publisher matches the installed LoggyBat;
#   - WidBar is closed (Windows cannot replace the files of a running widget);
#   - the terms of use: shown with a request for explicit consent unless this version was accepted before.
# Then records the acceptance (if new), installs the package over the old one with Add-AppxPackage
# (no uninstall) and starts WidBar again. Nothing is downloaded by this script.
param(
    [switch]$AcceptTerms,
    [string]$Package   # the LoggyBat .msix; default: the only .msix next to this script
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LoggyBat-Terms.ps1')
$widBarPackage = '30113AndreaDelBello.WidBar-WidgetTaskbarsystem'

if (-not $Package) {
    $found = @(Get-ChildItem $PSScriptRoot -File -Filter *.msix)
    if ($found.Count -ne 1) { throw "Expected exactly one .msix next to Update-LoggyBat.ps1, found $($found.Count)." }
    $Package = $found[0].FullName
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($Package)
try {
    $reader = New-Object IO.StreamReader($zip.GetEntry('AppxManifest.xml').Open())
    try { $id = ([xml]$reader.ReadToEnd()).Package.Identity } finally { $reader.Dispose() }
} finally { $zip.Dispose() }

function Stop-WithMessage([string]$ru, [string]$en, [int]$code) {
    Write-Host $ru -ForegroundColor Yellow
    Write-Host $en -ForegroundColor Yellow
    Write-Host 'Nothing was changed. / Ничего не изменено.'
    exit $code
}

$installed = @(Get-AppxPackage -Name $id.Name | Where-Object Publisher -eq $id.Publisher)
if (-not $installed) {
    Stop-WithMessage 'LoggyBat не установлен. Для первой установки используйте комплект первой установки (Install-LoggyBat.ps1).' `
        'LoggyBat is not installed. Use the first-install kit (Install-LoggyBat.ps1) for a first installation.' 1
}
if ([version]$installed[0].Version -ge [version]$id.Version) {
    Stop-WithMessage "Установлена версия $($installed[0].Version); этот пакет $($id.Version) не новее." `
        "Version $($installed[0].Version) is installed; this package $($id.Version) is not newer." 0
}
$signature = Get-AuthenticodeSignature $Package
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -ne $id.Publisher) {
    Stop-WithMessage "Подпись пакета не подтверждена ($($signature.Status))." "The package signature is not valid ($($signature.Status))." 1
}
if (Get-Process -Name 'WidBar', 'LoggyBat' -ErrorAction SilentlyContinue) {
    Stop-WithMessage 'WidBar запущен. Полностью закройте WidBar (значок в трее → выход) и запустите Update-LoggyBat.ps1 снова.' `
        'WidBar is running. Close WidBar completely (tray icon > Exit) and run Update-LoggyBat.ps1 again.' 3
}

$terms = Get-TermsInfo $PSScriptRoot
$newConsent = -not (Test-TermsAccepted $terms)
if ($newConsent -and -not $AcceptTerms -and -not (Request-TermsConsent $terms)) { Write-Declined; exit 2 }
if (-not $newConsent) { "Terms of use version $($terms.Version) were accepted before. / Условия версии $($terms.Version) уже приняты." }
if ($newConsent) { Save-TermsAcceptance $terms }

"Updating LoggyBat $($installed[0].Version) -> $($id.Version) ..."
Add-AppxPackage -Path $Package
$now = (Get-AppxPackage -Name $id.Name | Where-Object Publisher -eq $id.Publisher).Version
"Installed: LoggyBat $now"

$wb = Get-AppxPackage -Name $widBarPackage -ErrorAction SilentlyContinue | Select-Object -First 1
if ($wb) {
    $appId = ([xml](Get-Content (Join-Path $wb.InstallLocation 'AppxManifest.xml') -Raw)).Package.Applications.Application | Select-Object -First 1 | ForEach-Object Id
    Start-Process 'explorer.exe' "shell:AppsFolder\$($wb.PackageFamilyName)!$appId"
    'WidBar started again. / WidBar запущен снова.'
} else {
    'Start WidBar again. / Запустите WidBar снова.'
}
