<#
.SYNOPSIS  Measures FreeRGB CPU/memory while a software effect runs, against a SIMULATED OpenRGB server (not hardware).
.DESCRIPTION
  Builds nothing: pass an already-published FreeRGB.exe. Starts tools/FreeRGB.SimServer (60-LED strips by default),
  writes an isolated data dir (FREERGB_DATA_DIR) whose active profile runs the given software effect on every sim
  strip, launches FreeRGB --minimized, then samples CPU and memory.
  What this measures: FreeRGB's own cost of computing frames and writing them to a local socket. It does NOT include
  real USB traffic or OpenRGB's own cost, which depend on the hardware.
.EXAMPLE
  ./scripts/measure-effects.ps1 -Exe .\publish\FreeRGB.exe -Seconds 60 -Leds 60 -Fps 20
#>
param(
  [Parameter(Mandatory)] [string] $Exe,
  [int] $Seconds = 60,
  [int] $Leds = 60,
  [int] $Strips = 1,
  [int] $Fps = 20,
  [string] $Effect = 'Software: Rainbow'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$sim = Join-Path $root 'tools\FreeRGB.SimServer\bin\Release\net10.0\FreeRGB.SimServer.dll'
if (-not (Test-Path $sim)) { dotnet build (Join-Path $root 'tools\FreeRGB.SimServer') -c Release | Out-Null }

$portFile = Join-Path ([IO.Path]::GetTempPath()) ("freergb-simport-" + [guid]::NewGuid().ToString('N'))
$countFile = "$portFile.count"
$simProc = Start-Process dotnet -ArgumentList "`"$sim`"", 0, $Leds, $Strips, "`"$portFile`"", "`"$countFile`"" -PassThru -WindowStyle Hidden
while (-not (Test-Path $portFile) -or -not (Get-Content $portFile -ErrorAction SilentlyContinue)) { Start-Sleep -Milliseconds 100 }
$port = [int](Get-Content $portFile); Remove-Item $portFile

$data = Join-Path ([IO.Path]::GetTempPath()) ("freergb-measure-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory "$data\profiles" -Force | Out-Null
@{ openRgbPort = $port; effectFps = $Fps; activeProfile = 'Default'; startMinimized = $true } | ConvertTo-Json | Set-Content "$data\settings.json"
$devs = [ordered]@{}
0..($Strips - 1) | ForEach-Object { $devs["Sim Strip $_|SIM$_|HID: sim"] = @{ on = $true; color = '#FF0000'; brightness = 100; effect = $Effect; speed = 50; zones = @{} } }
@{ version = 1; name = 'Default'; devices = $devs } | ConvertTo-Json -Depth 6 | Set-Content "$data\profiles\Default.json"

$env:FREERGB_DATA_DIR = $data
$app = Start-Process $Exe -ArgumentList '--minimized' -PassThru
Start-Sleep 8   # let it connect and start the effect
$cpus = [Environment]::ProcessorCount
function Snap($p) { $p.Refresh(); [pscustomobject]@{ Cpu = $p.TotalProcessorTime.TotalSeconds; WS = $p.WorkingSet64; Priv = $p.PrivateMemorySize64 } }
function RxCount { try { [int](Get-Content $countFile -ErrorAction Stop) } catch { 0 } }
$a0 = Snap $app; $s0 = Snap $simProc; $t = [Diagnostics.Stopwatch]::StartNew(); $rx0 = RxCount
$peak = 0
$series = @(); $nextSample = 0
while ($t.Elapsed.TotalSeconds -lt $Seconds) {
  Start-Sleep 1; $app.Refresh(); $peak = [Math]::Max($peak, $app.WorkingSet64)
  if ($t.Elapsed.TotalSeconds -ge $nextSample) { $series += "{0,4}s:{1,6:N1}" -f [int]$t.Elapsed.TotalSeconds, ($app.PrivateMemorySize64 / 1MB); $nextSample += [Math]::Max(10, [int]($Seconds / 12)) }
}
$a1 = Snap $app; $s1 = Snap $simProc; $el = $t.Elapsed.TotalSeconds; $rx1 = RxCount

"Machine: $((Get-CimInstance Win32_Processor).Name), $cpus logical CPUs"
"Setup: $Strips strip(s) x $Leds LEDs, effect '$Effect', fps cap $Fps, window $([math]::Round($el)) s, SIMULATED server on port $port"
[pscustomobject]@{
  FreeRGB_CpuPercent_of_total = [math]::Round((($a1.Cpu - $a0.Cpu) / $el / $cpus) * 100, 3)
  FreeRGB_CpuPercent_of_1core = [math]::Round((($a1.Cpu - $a0.Cpu) / $el) * 100, 2)
  FreeRGB_WorkingSetMB        = [math]::Round($a1.WS / 1MB, 1)
  FreeRGB_PeakWSMB            = [math]::Round($peak / 1MB, 1)
  FreeRGB_PrivateMB           = [math]::Round($a1.Priv / 1MB, 1)
  WS_growth_MB_over_window    = [math]::Round(($a1.WS - $a0.WS) / 1MB, 1)
  UpdateLeds_packets_per_second = [math]::Round(($rx1 - $rx0) / $el, 1)
  SimServer_CpuPercent_of_1core = [math]::Round((($s1.Cpu - $s0.Cpu) / $el) * 100, 2)
} | Format-List | Out-String
"Private MB over time: " + ($series -join "  ")

Stop-Process $app -Force; Stop-Process $simProc -Force
Remove-Item Env:FREERGB_DATA_DIR
Start-Sleep 1; Remove-Item $data, $countFile -Recurse -Force -ErrorAction SilentlyContinue
