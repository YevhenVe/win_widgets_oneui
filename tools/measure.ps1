# Dev helper: restarts the given build, waits for it to settle and prints its footprint.
param([string]$Exe, [int]$Settle = 25)
Stop-Process -Name DesktopWidgets -ErrorAction SilentlyContinue
Start-Sleep 1
Start-Process $Exe
Start-Sleep $Settle
$p = Get-Process DesktopWidgets
$c0 = $p.TotalProcessorTime; Start-Sleep 20; $p.Refresh()
"WorkingSet {0:N0} MB | Private {1:N0} MB | Threads {2} | CPU {3:N3}%" -f ($p.WorkingSet64/1MB), ($p.PrivateMemorySize64/1MB), $p.Threads.Count, (($p.TotalProcessorTime-$c0).TotalSeconds/20/[Environment]::ProcessorCount*100)
