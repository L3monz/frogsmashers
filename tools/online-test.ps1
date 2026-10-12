# Starts two windowed copies of the development build that play each other online on this PC:
# one hosts on a port, the other joins it on 127.0.0.1.
#
#   powershell -ExecutionPolicy Bypass -File tools\online-test.ps1 [-Latency 100] [-Loss 5] [-Port 9100] [-LeaveAfter 30] [-Bot]
#
# -Latency and -Loss make LiteNetLib delay and drop packets on purpose, to see Rollbacks.
# -LeaveAfter N ends the Match after N seconds, so both copies save their replay recordings
# (persistentDataPath\replays); compare them with tools/compare-recordings.py.
# -Bot makes both frogs play scripted input, for a test nobody has to play.
# Each copy writes its own log next to the exe: host.log and join.log.
# Needs a development build (tools/build.sh dev): release builds run one copy at a time.
param([int]$Latency = 0, [int]$Loss = 0, [int]$Port = 9100, [int]$LeaveAfter = 0, [switch]$Bot)

$dir = Join-Path $PSScriptRoot "..\builds\dev"
$exe = Join-Path $dir "FrogSmashers.exe"
if (-not (Test-Path $exe)) { throw "No development build at $exe. Run tools/build.sh dev first." }

$common = @("-screen-fullscreen", "0", "-screen-width", "800", "-screen-height", "450",
            "-online-latency", "$Latency", "-online-loss", "$Loss", "-online-leave-after", "$LeaveAfter")
if ($Bot) { $common += "-online-bot" }

Start-Process -FilePath $exe -ArgumentList ($common + @("-logFile", (Join-Path $dir "host.log"), "-online-host", "$Port"))
Start-Sleep -Seconds 2
Start-Process -FilePath $exe -ArgumentList ($common + @("-logFile", (Join-Path $dir "join.log"), "-online-join", "127.0.0.1:$Port"))
Write-Host "Started host and join (port $Port, latency $Latency ms, loss $Loss%). Logs: $dir\host.log, join.log"
