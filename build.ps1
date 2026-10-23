<#
  Builds everything for a release of Opsis Media Player Pro:
    dist\app\                                     self-contained publish (.NET 10 + libvlc included)
    Output\OpsisMediaPlayerPro-<v>.zip            portable zip
    Output\OpsisMediaPlayerPro-Setup.exe          installer (Inno Setup 7; 6.x works too)
    *.sha256 next to each
  Usage: powershell -File build.ps1 [-SkipTests]
#>
param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Set-Location $root
$version = ([xml](Get-Content "$root\OpsisMediaPlayerPro.csproj")).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1

dotnet build OpsisMediaPlayerPro.csproj -c Release -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
if (-not $SkipTests) {
    dotnet test "$root\tests\SubtitleTests" -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Subtitle unit tests failed' }
    python "$root\tests\audit-menus.py"
    if ($LASTEXITCODE -ne 0) { throw 'Menu audit failed' }
    powershell -NoProfile -ExecutionPolicy Bypass -File "$root\tests\run-selftest.ps1"
    if ($LASTEXITCODE -ne 0) { throw 'Self-test failed' }
}

Remove-Item "$root\dist" -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish OpsisMediaPlayerPro.csproj -c Release -r win-x64 --self-contained -o "$root\dist\app" -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }

New-Item -ItemType Directory -Force "$root\Output" | Out-Null
$zip = "$root\Output\OpsisMediaPlayerPro-$version.zip"
Remove-Item $zip -Force -ErrorAction SilentlyContinue
Compress-Archive -Path "$root\dist\app\*" -DestinationPath $zip

$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 7\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe", "${env:ProgramFiles}\Inno Setup 7\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { Write-Warning 'Inno Setup not found: installer skipped'; }
else {
    & $iscc "/DDistDir=$root\dist\app" "$root\installer\OpsisMediaPlayerPro.iss"
    if ($LASTEXITCODE -ne 0) { throw 'Installer failed' }
}
foreach ($f in Get-ChildItem "$root\Output\*" -Include *.zip, *.exe | Where-Object { $_.Name -match [regex]::Escape($version) -or $_.Name -like '*Setup.exe' }) {
    $h = (Get-FileHash $f.FullName -Algorithm SHA256).Hash.ToLower()
    "$h *$($f.Name)" | Set-Content "$($f.FullName).sha256" -Encoding ascii
    "$($f.Name)  $h"
}
