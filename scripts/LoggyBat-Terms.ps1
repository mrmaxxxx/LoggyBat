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

# Every step stops on an error, whatever the caller's $ErrorActionPreference, and the result is read
# back: success is reported only when the registry really holds this version and hash. An existing key
# is kept as it is (New-Item -Force would empty it) and only these three values are written.
function Save-TermsAcceptance($terms) {
    if (-not (Test-Path $script:TermsKey)) { New-Item -Path $script:TermsKey -ErrorAction Stop | Out-Null }
    Set-ItemProperty -Path $script:TermsKey -Name TermsVersion -Value $terms.Version -ErrorAction Stop
    Set-ItemProperty -Path $script:TermsKey -Name TermsSha256 -Value $terms.Sha256 -ErrorAction Stop
    Set-ItemProperty -Path $script:TermsKey -Name AcceptedUtc -Value ([DateTime]::UtcNow.ToString('o')) -ErrorAction Stop
    $saved = Get-ItemProperty -Path $script:TermsKey -ErrorAction Stop
    if ("$($saved.TermsVersion)" -ne "$($terms.Version)" -or "$($saved.TermsSha256)" -ne $terms.Sha256) {
        throw "The acceptance of the terms was not saved: the registry holds version '$($saved.TermsVersion)'. / Принятие условий не сохранено: в реестре версия '$($saved.TermsVersion)'."
    }
    Write-Host "Acceptance of terms version $($terms.Version) saved ($($script:TermsKey)). / Принятие условий версии $($terms.Version) сохранено."
}

function Write-Declined {
    Write-Host 'Terms not accepted. Nothing was installed or changed. / Условия не приняты. Ничего не установлено и не изменено.' -ForegroundColor Yellow
}
