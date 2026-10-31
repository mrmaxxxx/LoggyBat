# Shared by Install-LoggyBat.ps1 and Update-LoggyBat.ps1 (dot-sourced). Shows TERMS.md from the kit and
# asks for explicit consent. Nothing on the computer is changed until Save-TermsAcceptance is called,
# which the scripts do only after consent and after all their checks have passed.
# The acceptance record is HKCU:\Software\LoggyBat (TermsVersion, TermsSha256, AcceptedUtc).

$script:TermsKey = 'HKCU:\Software\LoggyBat'

function Get-TermsInfo([string]$folder) {
    $path = Join-Path $folder 'TERMS.md'
    if (-not (Test-Path $path)) { throw "TERMS.md not found next to the script ($folder)." }
    $text = Get-Content $path -Raw -Encoding UTF8
    if ($text -notmatch 'Terms version:\s*(\d+)') { throw 'TERMS.md has no "Terms version:" line.' }
    [pscustomobject]@{
        Path    = $path
        Text    = $text
        Version = $Matches[1]
        Sha256  = (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

function Test-TermsAccepted($terms) {
    $saved = Get-ItemProperty $script:TermsKey -ErrorAction SilentlyContinue
    return ($saved -and "$($saved.TermsVersion)" -eq $terms.Version)
}

# Returns $true only for an explicit yes / да.
function Request-TermsConsent($terms) {
    Write-Host ''
    Write-Host $terms.Text
    Write-Host ''
    Write-Host "Terms of use: $($terms.Path)"
    Write-Host "Licenses and notices of the components: $(Join-Path (Split-Path $terms.Path) 'Notices')"
    Write-Host ''
    $answer = Read-Host 'Do you accept these terms? Type YES to accept / Принимаете условия? Введите ДА'
    return ("$answer".Trim() -in 'yes', 'да')
}

function Save-TermsAcceptance($terms) {
    New-Item -Path $script:TermsKey -Force | Out-Null
    Set-ItemProperty -Path $script:TermsKey -Name TermsVersion -Value $terms.Version
    Set-ItemProperty -Path $script:TermsKey -Name TermsSha256 -Value $terms.Sha256
    Set-ItemProperty -Path $script:TermsKey -Name AcceptedUtc -Value ([DateTime]::UtcNow.ToString('o'))
}

function Write-Declined {
    Write-Host 'Terms not accepted. Nothing was installed or changed. / Условия не приняты. Ничего не установлено и не изменено.' -ForegroundColor Yellow
}
