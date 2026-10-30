# Fails the build unless the WinUI package version is the expected one, both as resolved by NuGet
# and as recorded in the deps.json that ships inside the MSIX.
# Why: Microsoft.WindowsAppSDK.WinUI 2.3.2, 2.3.0, 2.2.1, 2.1.0 and 2.0.12 carry an Engineering Preview
# license that prohibits redistribution; fixed in Windows App SDK 2.4.0, which resolves WinUI 2.3.6.
# Source: https://github.com/microsoft/WindowsAppSDK/issues/6654 (Microsoft reply, 2026-08-14).
param(
    [Parameter(Mandatory)][string]$Msix,
    [string]$Project,            # the .csproj; when given, NuGet's resolution is checked too
    [string]$Expected = '2.3.6'
)

$ErrorActionPreference = 'Stop'
$affected = '2.3.2', '2.3.0', '2.2.1', '2.1.0', '2.0.12'
$package = 'Microsoft.WindowsAppSDK.WinUI'

function Assert-WinUI([string[]]$versions, [string]$source) {
    "${source}: $package $($versions -join ', ')"
    # Also as a GitHub annotation: those are readable without signing in, unlike the log.
    if ($env:GITHUB_ACTIONS) { "::notice title=WinUI::${source}: $package $($versions -join ', ')" }
    if ($versions.Count -ne 1) { throw "${source}: expected exactly one $package version, found $($versions.Count)." }
    if ($versions[0] -in $affected) { throw "${source}: $package $($versions[0]) has the Engineering Preview license (WindowsAppSDK#6654)." }
    if ($versions[0] -ne $Expected) { throw "${source}: $package $($versions[0]), expected $Expected." }
}

if ($Project) {
    # The check Microsoft suggests, printed for the log.
    dotnet list $Project package --include-transitive | Select-String 'Microsoft.WindowsAppSDK' | ForEach-Object { $_.Line.Trim() }
    if ($LASTEXITCODE -ne 0) {
        # Informational only: project.assets.json below is the same data and is what we assert on.
        "dotnet list exited with $LASTEXITCODE; checking project.assets.json directly."
        $global:LASTEXITCODE = 0
    }

    $assets = Join-Path (Split-Path $Project) 'obj\project.assets.json'
    $libraries = (Get-Content $assets -Raw | ConvertFrom-Json).libraries.PSObject.Properties.Name
    Assert-WinUI @($libraries | Where-Object { $_ -like "$package/*" } | ForEach-Object { $_.Split('/')[1] }) 'NuGet (project.assets.json)'
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($Msix)
try {
    $entry = @($zip.Entries | Where-Object { $_.FullName -like '*.deps.json' })
    if ($entry.Count -ne 1) { throw "Expected one .deps.json in $Msix, found $($entry.Count)." }
    $reader = New-Object IO.StreamReader($entry[0].Open())
    $deps = $reader.ReadToEnd() | ConvertFrom-Json
    $reader.Dispose()
} finally {
    $zip.Dispose()
}
$shipped = $deps.libraries.PSObject.Properties.Name | Where-Object { $_ -like "$package/*" } | ForEach-Object { $_.Split('/')[1] }
Assert-WinUI @($shipped) 'MSIX (deps.json)'
