# Creates the permanent LoggyBat signing certificate (CN=Max). Run once, by Max, on his PC.
# Result, in a folder OUTSIDE the repository:
#   LoggyBat-signing.pfx  private key + certificate: keep it safe, never commit it
#   LoggyBat.cer          public certificate: install into "Trusted People"
# The base64 of the .pfx is copied to the clipboard for the GitHub secret SIGNING_PFX_BASE64.

param(
    [string]$OutDir = (Join-Path $HOME 'LoggyBat-signing')
)

$ErrorActionPreference = 'Stop'

$pfxPath = Join-Path $OutDir 'LoggyBat-signing.pfx'
if (Test-Path $pfxPath) {
    throw "$pfxPath already exists. Delete it yourself if you really want a new certificate: updates of the installed app need the same one."
}

$password = Read-Host 'Придумайте пароль для сертификата (он же пойдёт в секрет SIGNING_PFX_PASSWORD)' -AsSecureString

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
    Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $password | Out-Null
    Export-Certificate -Cert $cert -FilePath (Join-Path $OutDir 'LoggyBat.cer') | Out-Null
}
finally {
    # The .pfx file is now the only copy of the private key.
    Remove-Item "Cert:\CurrentUser\My\$($cert.Thumbprint)"
}

[Convert]::ToBase64String([IO.File]::ReadAllBytes($pfxPath)) | Set-Clipboard

Write-Host ''
Write-Host "Готово: $OutDir"
Write-Host "Отпечаток: $($cert.Thumbprint), действует до $($cert.NotAfter.ToString('d'))"
Write-Host 'Base64 сертификата скопирован в буфер обмена: вставьте его в секрет SIGNING_PFX_BASE64.'
