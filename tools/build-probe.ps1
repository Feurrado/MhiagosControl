# Compila a sonda do protocolo (console, sem elevacao).
#   powershell -ExecutionPolicy Bypass -File .\tools\build-probe.ps1
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$fw   = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"
$csc  = Join-Path $fw 'csc.exe'
$out  = Join-Path $root 'bin\Probe.exe'

New-Item -ItemType Directory -Force -Path (Join-Path $root 'bin') | Out-Null

& $csc /nologo /target:exe /platform:x64 /codepage:65001 ("/out:" + $out) `
    ("/reference:" + (Join-Path $fw 'System.dll')) `
    (Join-Path $root 'src\HidPanel.cs') `
    (Join-Path $root 'tools\Probe.cs')

if (Test-Path $out) { Write-Output ("OK -> " + $out) } else { Write-Output "FALHOU"; exit 1 }
