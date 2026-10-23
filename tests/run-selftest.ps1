<#
  Runs the player self-test: every button and menu item is executed against a real playback.
  Creates a 2-minute stereo test tone, starts OpsisMediaPlayerPro.exe --selftest and prints the report.
  Exit code 0 = all passed.   Usage: powershell -File tests\run-selftest.ps1
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exe = Get-ChildItem "$root\bin\Release" -Recurse -Filter OpsisMediaPlayerPro.exe | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $exe) { throw 'Build first: dotnet build -c Release' }

# 120 s, 22.05 kHz, 16-bit stereo sine tones
$wav = Join-Path ([IO.Path]::GetTempPath()) 'opsis_selftest_tone.wav'
$rate = 22050; $frames = $rate * 120
$ms = New-Object IO.MemoryStream; $w = New-Object IO.BinaryWriter($ms)
$w.Write([Text.Encoding]::ASCII.GetBytes('RIFF')); $w.Write([int](36 + $frames * 4)); $w.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt '))
$w.Write([int]16); $w.Write([int16]1); $w.Write([int16]2); $w.Write([int]$rate); $w.Write([int]($rate * 4)); $w.Write([int16]4); $w.Write([int16]16)
$w.Write([Text.Encoding]::ASCII.GetBytes('data')); $w.Write([int]($frames * 4))
for ($i = 0; $i -lt $frames; $i++) { $w.Write([int16](3000 * [Math]::Sin(2 * [Math]::PI * 440 * $i / $rate))); $w.Write([int16](3000 * [Math]::Sin(2 * [Math]::PI * 660 * $i / $rate))) }
$w.Flush(); [IO.File]::WriteAllBytes($wav, $ms.ToArray())

$report = Join-Path ([IO.Path]::GetTempPath()) 'opsis_mediaplayer_selftest.txt'
Remove-Item $report -ErrorAction SilentlyContinue
$p = Start-Process $exe.FullName -ArgumentList @('--selftest', "`"$wav`"") -PassThru
if (-not $p.WaitForExit(120000)) { $p.Kill(); throw 'Self-test timed out' }
if (Test-Path $report) { Get-Content $report | Where-Object { $_ -notmatch '^SKIP' } } else { Write-Error 'No report was written' }
exit $p.ExitCode
