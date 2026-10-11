# Fixes Unity 2019.2's IL2CPP sockets when the Windows 10 SDK 10.0.19041 (or newer) is installed.
#
# Symptom: in IL2CPP builds every UDP socket fails with
#   ArgumentException: Value does not fall within the expected range.
#   at System.Net.Sockets.Socket.SetSocketOption ... at Socket.SocketDefaults ()
# so online play can't start. (Unity fixed this in 2019.4.5 and 2020.1.1; 2019.2 never got it.)
#
# Cause: that SDK added "#define SOL_IP (SOL_SOCKET-4)" and SOL_IPV6. IL2CPP's Windows socket
# code uses SOL_IP as the setsockopt level whenever it's defined, instead of IPPROTO_IP, and
# Windows rejects it. This makes IL2CPP use IPPROTO_IP / IPPROTO_IPV6, as it did before.
#
# Run once per Unity install, in PowerShell as Administrator (the file is under Program Files):
#   powershell -ExecutionPolicy Bypass -File tools\patch-il2cpp-sockets.ps1
# Keeps a backup next to the file. Safe to run again. Then delete
# HohimBrueh\Library\il2cpp_cache (if it exists) so the next build recompiles IL2CPP's runtime.

$ErrorActionPreference = "Stop"
$file = "C:\Program Files\Unity\Hub\Editor\2019.2.12f1\Editor\Data\il2cpp\libil2cpp\os\Win32\SocketImpl.cpp"
if (-not (Test-Path $file)) { throw "Not found: $file" }

$text = [IO.File]::ReadAllText($file)
if ($text.Contains("PATCHED-SOL_IP")) { Write-Host "Already patched."; exit 0 }

$backup = "$file.orig"
if (-not (Test-Path $backup)) { Copy-Item $file $backup }

$patched = $text.Replace("#ifdef SOL_IP`r`n", "#if 0 // PATCHED-SOL_IP: Windows SDK 19041's SOL_IP isn't a setsockopt level`r`n")
$patched = $patched.Replace("#ifdef SOL_IP`n", "#if 0 // PATCHED-SOL_IP: Windows SDK 19041's SOL_IP isn't a setsockopt level`n")
$patched = $patched.Replace("#ifdef SOL_IPV6", "#if 0 // PATCHED-SOL_IPV6")
if ($patched -eq $text) { throw "The expected lines weren't found; not patched." }

$attributes = (Get-Item $file).Attributes
Set-ItemProperty $file -Name IsReadOnly -Value $false
[IO.File]::WriteAllText($file, $patched)
(Get-Item $file).Attributes = $attributes
Write-Host "Patched $file (backup: $backup)."
