# Creates the permanent LoggyBat development signing certificate (CN=Max,
# must equal Publisher in src\LoggyBat.Package\Package.appxmanifest).
# Run once, by Max, on his PC. Result, in a folder OUTSIDE the repository:
#   LoggyBat-signing.pfx  private key, password-protected: keep it safe, never commit or share it
#   LoggyBat.cer          public certificate: install into "Trusted People"
# The script prints no key and no password; copying the key to GitHub is a separate step.

param(
    [string]$OutDir = (Join-Path $HOME 'LoggyBat-signing')
)

$ErrorActionPreference = 'Stop'

$OutDir = [IO.Path]::GetFullPath($OutDir)
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (($OutDir + '\').StartsWith($repo + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw "Папка $OutDir внутри репозитория. Ключ нельзя хранить в репозитории: укажите другую папку."
}

$pfxPath = Join-Path $OutDir 'LoggyBat-signing.pfx'
if (Test-Path $pfxPath) {
    throw "$pfxPath уже есть. Новый сертификат не создан: обновления установленного приложения требуют прежний."
}

function Plain([securestring]$s) { [Net.NetworkCredential]::new('', $s).Password }

$password = Read-Host 'Придумайте пароль для сертификата (не меньше 12 символов)' -AsSecureString
$again = Read-Host 'Повторите пароль' -AsSecureString
if ((Plain $password).Length -lt 12) { throw 'Пароль короче 12 символов. Ничего не создано.' }
if ((Plain $password) -cne (Plain $again)) { throw 'Пароли не совпадают. Ничего не создано.' }

New-Item -ItemType Directory -Force $OutDir | Out-Null

$cert = New-SelfSignedCertificate `
    -Type Custom `
    -Subject 'CN=Max' `
    -FriendlyName 'LoggyBat Signing' `
    -KeyUsage DigitalSignature `
    -KeyAlgorithm RSA `
    -KeyLength 2048 `
    -HashAlgorithm SHA256 `
    -KeyExportPolicy Exportable `
    -NotAfter (Get-Date).AddYears(10) `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -TextExtension @(
        '2.5.29.37={text}1.3.6.1.5.5.7.3.3',  # code signing
        '2.5.29.19={text}'                    # not a CA
    )

try {
    Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $password `
        -CryptoAlgorithmOption AES256_SHA256 | Out-Null
    Export-Certificate -Cert $cert -FilePath (Join-Path $OutDir 'LoggyBat.cer') | Out-Null
}
finally {
    # The .pfx file is now the only copy of the private key.
    Remove-Item "Cert:\CurrentUser\My\$($cert.Thumbprint)"
}

Write-Host ''
Write-Host "Готово: $OutDir"
Write-Host "Владелец: $($cert.Subject), отпечаток: $($cert.Thumbprint), действует до $($cert.NotAfter.ToString('d'))"
