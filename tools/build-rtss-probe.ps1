# Compila o diagnostico da ponte com o RTSS. Console e autonomo: nao depende
# dos fontes do aplicativo, para poder ser levado a outra maquina sozinho.
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$fw   = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"
$csc  = Join-Path $fw 'csc.exe'
$bin  = Join-Path $root 'bin'
$out  = Join-Path $bin 'RtssProbe.exe'

if (-not (Test-Path $csc)) { Write-Output "ERRO: csc.exe nao encontrado em $fw"; exit 1 }
New-Item -ItemType Directory -Force -Path $bin | Out-Null

$arguments = @(
    '/nologo', '/target:exe', '/platform:x64', '/codepage:65001',
    ('/out:' + $out),
    ('/reference:' + (Join-Path $fw 'System.dll')),
    ('/reference:' + (Join-Path $fw 'System.Core.dll')),
    (Join-Path $root 'tools\RtssProbe.cs')
)

& $csc $arguments 2>&1 | Out-String | Write-Output
if ($LASTEXITCODE -ne 0) { Write-Output "FALHOU ao compilar"; exit 1 }
Write-Output ("OK -> " + $out)
