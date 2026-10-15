# Compila e roda a suite de testes. Console, nao winexe: como winexe a saida
# some e uma falha vira apenas um codigo de saida negativo.
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$fw   = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"
$csc  = Join-Path $fw 'csc.exe'
$bin  = Join-Path $root 'bin'
$out  = Join-Path $bin 'Tests.exe'

if (-not (Test-Path $csc)) { Write-Output "ERRO: csc.exe nao encontrado em $fw"; exit 1 }
New-Item -ItemType Directory -Force -Path $bin | Out-Null
Get-ChildItem (Join-Path $root 'lib') -Filter *.dll | Where-Object { $_.Name -ne 'api-ms-win-core-sysinfo-825-64.dll' } | Copy-Item -Destination (Join-Path $root 'bin') -Force -ErrorAction SilentlyContinue

$refs = @(
    (Join-Path $fw 'System.dll'),
    (Join-Path $fw 'System.Core.dll'),
    (Join-Path $fw 'System.Drawing.dll'),
    (Join-Path $fw 'System.Windows.Forms.dll'),
    (Join-Path $fw 'System.Management.dll'),
    (Join-Path $root 'lib\LibreHardwareMonitorLib.dll')
)

# Os testes compilam contra os fontes do aplicativo, e nao contra o executavel:
# assim alcancam o que e interno (WriteField) sem abrir a visibilidade so para
# poder testar.
$srcs = @(Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName })
$srcs += (Join-Path $root 'tools\Tests.cs')
$srcs += (Join-Path $root 'tools\RegressionTests.cs')
$srcs += (Join-Path $root 'tools\EvolutionTests.cs')
$srcs += (Join-Path $root 'tools\BugFixTests.cs')
$srcs += (Join-Path $root 'tools\PageTests.cs')
$srcs += (Join-Path $root 'tools\RevisaoTests.cs')
$srcs += (Join-Path $root 'tools\CapturaDeEscala.cs')
$srcs += (Join-Path $root 'tools\AnimacaoTests.cs')
$srcs += (Join-Path $root 'tools\Installer.cs')

$arguments = @(
    '/nologo', '/target:exe', '/platform:x64', '/codepage:65001',
    '/main:MhiagosControl.Tests',
    ('/out:' + $out),
    ('/resource:' + (Join-Path $root 'assets\tray.ico')            + ',MhiagosControl.tray.ico'),
    ('/resource:' + (Join-Path $root 'assets\MhiagosControl.ico')  + ',MhiagosControl.app.ico'),
    ('/resource:' + (Join-Path $root 'assets\cooler.png')          + ',MhiagosControl.cooler.png'),
    ('/resource:' + (Join-Path $root 'assets\g325.png')            + ',MhiagosControl.g325.png'),
    ('/resource:' + (Join-Path $root 'assets\x11.png')             + ',MhiagosControl.x11.png'),
    ('/resource:' + (Join-Path $root 'assets\bluetooth.png')       + ',MhiagosControl.bluetooth.png')
)
foreach ($r in $refs) { $arguments += ('/reference:' + $r) }
$arguments += $srcs

& $csc $arguments 2>&1 | Out-String | Write-Output
if ($LASTEXITCODE -ne 0) { Write-Output "FALHOU ao compilar os testes"; exit 1 }

& $out
exit $LASTEXITCODE
