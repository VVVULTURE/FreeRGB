<#
.SYNOPSIS  Reproducible CPU/memory/startup measurement for FreeRGB (Windows).
.DESCRIPTION
  Launches the given FreeRGB.exe, records time-to-window, then samples working set / private bytes and
  CPU time over a window. CPU% = (delta CPU seconds) / (wall seconds) / logical processors * 100.
  Use -Minimized to measure the start-at-login (tray only) state, which never creates a window.
  The process is killed at the end. Close other heavy programs first; results depend on the machine.
.EXAMPLE
  ./scripts/measure-perf.ps1 -Exe .\publish\FreeRGB.exe -IdleSeconds 60
#>
param(
  [Parameter(Mandatory)] [string] $Exe,
  [int] $IdleSeconds = 60,
  [switch] $Minimized
)
$ErrorActionPreference = 'Stop'
$procArgs = @(); if ($Minimized) { $procArgs += '--minimized' }
$cpus = [Environment]::ProcessorCount

function Sample($p, $seconds, $label) {
  $p.Refresh(); $c0 = $p.TotalProcessorTime.TotalSeconds; $t0 = [Diagnostics.Stopwatch]::StartNew()
  $peak = 0; $ws = @()
  while ($t0.Elapsed.TotalSeconds -lt $seconds) { Start-Sleep -Milliseconds 500; $p.Refresh(); $ws += $p.WorkingSet64; $peak = [Math]::Max($peak, $p.WorkingSet64) }
  $p.Refresh(); $c1 = $p.TotalProcessorTime.TotalSeconds
  [pscustomobject]@{
    State       = $label
    Seconds     = [math]::Round($t0.Elapsed.TotalSeconds, 1)
    CpuPercent  = [math]::Round((($c1 - $c0) / $t0.Elapsed.TotalSeconds / $cpus) * 100, 3)
    WorkingSetMB= [math]::Round(($ws | Measure-Object -Average).Average / 1MB, 1)
    PeakWSMB    = [math]::Round($peak / 1MB, 1)
    PrivateMB   = [math]::Round($p.PrivateMemorySize64 / 1MB, 1)
    Threads     = $p.Threads.Count
    Handles     = $p.HandleCount
  }
}

$sw = [Diagnostics.Stopwatch]::StartNew()
$p = if ($procArgs.Count) { Start-Process $Exe -ArgumentList $procArgs -PassThru } else { Start-Process $Exe -PassThru }
$ttw = $null
if (-not $Minimized) {
  while ($sw.Elapsed.TotalSeconds -lt 30) { $p.Refresh(); if ($p.MainWindowHandle -ne 0) { $ttw = $sw.ElapsedMilliseconds; break }; Start-Sleep -Milliseconds 20 }
}
"Machine: $((Get-CimInstance Win32_Processor).Name), $cpus logical CPUs, $([math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory/1GB,1)) GB RAM, $((Get-CimInstance Win32_OperatingSystem).Caption)"
"Exe: $Exe  ($([math]::Round((Get-Item $Exe).Length/1MB,1)) MB main exe)  Minimized=$Minimized"
if ($ttw) { "Time to main window: $ttw ms" }
$rows = @()
$rows += Sample $p 5 'first 5 s after launch'
$rows += Sample $p $IdleSeconds "idle ($IdleSeconds s)"
$rows | Format-Table -AutoSize | Out-String -Width 200
Stop-Process $p -Force
