# Compila a sonda do protocolo (console, sem elevacao).
#   powershell -ExecutionPolicy Bypass -File .\tools\build-probe.ps1
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$fw   = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"
$csc  = Join-Path $fw 'csc.exe'
$out  = Join-Path $root 'bin\Probe.exe'

New-Item -ItemType Directory -Force -Path (Join-Path $root 'bin') | Out-Null

# Compila contra os fontes inteiros, como a suite de testes: o HidPanel implementa
# IPanelDevice, que vive em RuntimeContracts.cs junto de ISensorService, e este
# depende de SensorEntry - a cadeia inteira vem junto. /main: desempata os dois
# pontos de entrada, o do aplicativo e o desta sonda.
$refs = @(
    (Join-Path $fw 'System.dll'),
    (Join-Path $fw 'System.Core.dll'),
    (Join-Path $fw 'System.Drawing.dll'),
    (Join-Path $fw 'System.Windows.Forms.dll'),
    (Join-Path $fw 'System.Management.dll'),
    (Join-Path $root 'lib\LibreHardwareMonitorLib.dll')
)
$srcs = @(Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName })
$srcs += (Join-Path $root 'tools\Probe.cs')

$arguments = @(
    '/nologo', '/target:exe', '/platform:x64', '/codepage:65001',
    '/main:MhiagosControl.Probe',
    ('/out:' + $out),
    ('/resource:' + (Join-Path $root 'assets\tray.ico')           + ',MhiagosControl.tray.ico'),
    ('/resource:' + (Join-Path $root 'assets\MhiagosControl.ico') + ',MhiagosControl.app.ico'),
    ('/resource:' + (Join-Path $root 'assets\cooler.png')         + ',MhiagosControl.cooler.png')
)
foreach ($r in $refs) { $arguments += ('/reference:' + $r) }
$arguments += $srcs

Copy-Item (Join-Path $root 'lib\LibreHardwareMonitorLib.dll') (Join-Path $root 'bin') -Force -ErrorAction SilentlyContinue
& $csc $arguments 2>&1 | Out-String | Write-Output
$rc = $LASTEXITCODE

# O codigo de saida do compilador, e nao a existencia do arquivo: com uma sonda
# antiga em bin\, Test-Path dava OK numa compilacao que falhou e devolvia um
# binario de outra versao do protocolo. Mesmo cuidado do build.ps1.
if ($rc -eq 0 -and (Test-Path $out)) { Write-Output ("OK -> " + $out) }
else { Write-Output "FALHOU (csc saiu com $rc)"; exit 1 }
