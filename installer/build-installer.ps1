<#
 Builds the Windows installer:  powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1 [-Version 1.0.1]
 Needs: .NET 10 SDK and Inno Setup 6 (https://jrsoftware.org/isdl.php; or: winget install JRSoftware.InnoSetup).
 Optional: put the unzipped poppler folder in src\GanjoorPdfToGdb.WinForms\poppler first; it is shipped inside the app,
 so end users do not have to install poppler themselves.
#>
param([string]$Version = "1.0.0")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root "src\GanjoorPdfToGdb.WinForms\GanjoorPdfToGdb.WinForms.csproj"
$out  = Join-Path $root "publish\win-x64"

if (-not (Get-ChildItem -Path (Join-Path $root "src\GanjoorPdfToGdb.WinForms\poppler") -Filter pdftoppm.exe -Recurse -ErrorAction SilentlyContinue)) {
    Write-Warning "No poppler folder (pdftoppm.exe) found in src\GanjoorPdfToGdb.WinForms\poppler - the installer will not include it, users must install poppler themselves."
}

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish $proj -c Release -r win-x64 --self-contained true -p:Version=$Version -o $out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$iscc = @(
    (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source,
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 (ISCC.exe) was not found. Install it from https://jrsoftware.org/isdl.php" }

& $iscc "/DAppVersion=$Version" "/DSourceDir=$out" (Join-Path $PSScriptRoot "Pdf2GdbStudio.iss")
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed" }
Write-Host "Done: $(Join-Path $PSScriptRoot "output\Pdf2GdbStudio-Setup-$Version.exe")"
