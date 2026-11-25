<#
.SYNOPSIS
  Builds DM Enhancer and packages it into dist\:
    DmEnhancer.exe                      the standalone app (one file, nothing to install)
    DmEnhancer-<version>-portable.zip   the standalone app zipped
    DmEnhancer-Setup-<version>.exe      the installer (needs Inno Setup 6; skipped if it isn't installed)

.EXAMPLE
  ./build.ps1                  # version from DmEnhancer.csproj
  ./build.ps1 -Version 1.2.0
#>
param(
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

if (-not $Version) {
    $Version = ([xml](Get-Content DmEnhancer.csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
$Version = $Version.TrimStart("v")
Write-Host "Building DM Enhancer $Version"

Remove-Item bin\app, dist -Recurse -Force -ErrorAction SilentlyContinue
dotnet build DmEnhancer.csproj -c Release -o bin\app "-p:Version=$Version"
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

New-Item -ItemType Directory dist | Out-Null
Copy-Item bin\app\DmEnhancer.exe dist\
Compress-Archive -Path bin\app\DmEnhancer.exe, bin\app\DmEnhancer.exe.config -DestinationPath "dist\DmEnhancer-$Version-portable.zip"

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { $iscc = (Get-Command iscc -ErrorAction SilentlyContinue).Source }

if ($iscc) {
    & $iscc /Q "/DAppVersion=$Version" installer\DmEnhancer.iss
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed" }
} else {
    Write-Warning "Inno Setup 6 not found, so the installer was skipped. Install it with: winget install JRSoftware.InnoSetup"
}

Get-ChildItem dist | Format-Table Name, @{ n = "Size"; e = { "{0:N0} KB" -f ($_.Length / 1KB) } } -AutoSize
