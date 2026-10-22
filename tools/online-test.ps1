# Starts two windowed copies of the development build that play each other online on this PC.
#
#   powershell -ExecutionPolicy Bypass -File tools\online-test.ps1 [-Server] [-Relay] [-Latency 100] [-Loss 5] [-Port 9100] [-LeaveAfter 30] [-Bot] [-PointsToWin 1] [-Rendezvous 127.0.0.1:9050]
#
# Without -Server, one copy hosts on a port and the other joins it on 127.0.0.1 (no server).
# With -Server, they meet through the rendezvous server: the host creates a Room, this script
# reads the Room Code from the host's log, and the other copy joins with it. -Relay makes
# them use the server's relay instead of a direct connection. -Rendezvous host:port uses another
# server than the deployed one, such as a local copy of Hootenanny.
# -Latency and -Loss make LiteNetLib delay and drop packets on purpose, to see Rollbacks.
# -LeaveAfter N ends the Match after N seconds, so both copies save their replay recordings
# (persistentDataPath\replays); compare them with tools/compare-recordings.py.
# -Bot makes both frogs play scripted input, for a test nobody has to play.
# -PointsToWin N ends each Round at N points, so bots reach the Round end and the next Level.
# Each copy writes its own log next to the exe: host.log and join.log.
# Needs a development build (tools/build.sh dev): release builds run one copy at a time.
param([switch]$Server, [switch]$Relay, [int]$Latency = 0, [int]$Loss = 0, [int]$Port = 9100, [int]$LeaveAfter = 0, [switch]$Bot, [int]$PointsToWin = 0, [string]$Rendezvous = "")

$dir = Join-Path $PSScriptRoot "..\builds\dev"
$exe = Join-Path $dir "FrogSmashers.exe"
if (-not (Test-Path $exe)) { throw "No development build at $exe. Run tools/build.sh dev first." }

$common = @("-screen-fullscreen", "0", "-screen-width", "800", "-screen-height", "450",
            "-online-latency", "$Latency", "-online-loss", "$Loss", "-online-leave-after", "$LeaveAfter")
if ($Bot) { $common += "-online-bot" }
if ($PointsToWin -gt 0) { $common += @("-dev-points-to-win", "$PointsToWin") }

$hostLog = Join-Path $dir "host.log"
$joinLog = Join-Path $dir "join.log"
if ($Relay) { $common += "-online-relay" }
if ($Rendezvous) { $common += @("-rendezvous", $Rendezvous) }

if (-not $Server) {
    Start-Process -FilePath $exe -ArgumentList ($common + @("-logFile", $hostLog, "-online-host", "$Port"))
    Start-Sleep -Seconds 2
    Start-Process -FilePath $exe -ArgumentList ($common + @("-logFile", $joinLog, "-online-join", "127.0.0.1:$Port"))
    Write-Host "Started host and join (port $Port, latency $Latency ms, loss $Loss%). Logs: $hostLog, $joinLog"
    exit 0
}

if (Test-Path $hostLog) { Remove-Item $hostLog }
Start-Process -FilePath $exe -ArgumentList ($common + @("-logFile", $hostLog, "-online-create"))
$code = $null
for ($i = 0; $i -lt 60 -and -not $code; $i++) {
    Start-Sleep -Milliseconds 500
    if (Test-Path $hostLog) {
        $match = Select-String -Path $hostLog -Pattern "lobby: Room Code (\S+)" | Select-Object -First 1
        if ($match) { $code = $match.Matches[0].Groups[1].Value }
    }
}
if (-not $code) { throw "The host didn't get a Room Code within 30 s. See $hostLog." }
Start-Process -FilePath $exe -ArgumentList ($common + @("-logFile", $joinLog, "-online-code", $code))
Write-Host "Started host (Room $code) and join through the server (relay forced: $Relay, latency $Latency ms, loss $Loss%). Logs: $hostLog, $joinLog"
