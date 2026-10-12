# Compila a previa do widget de bateria (janela avulsa, sem elevacao).
#   powershell -ExecutionPolicy Bypass -File .\tools\build-widget-preview.ps1
#   .\bin\WidgetPreview.exe --segundos 30
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$fw   = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"
$csc  = Join-Path $fw 'csc.exe'
$out  = Join-Path $root 'bin\WidgetPreview.exe'

if (-not (Test-Path $csc)) { Write-Output "ERRO: csc.exe nao encontrado em $fw"; exit 1 }
New-Item -ItemType Directory -Force -Path (Join-Path $root 'bin') | Out-Null

# Contra os fontes inteiros, como a sonda e os testes: o widget usa a tira de
# perifericos, o tema e os textos. /main: desempata com o ponto de entrada do app.
$refs = @(
    (Join-Path $fw 'System.dll'),
    (Join-Path $fw 'System.Core.dll'),
    (Join-Path $fw 'System.Drawing.dll'),
    (Join-Path $fw 'System.Windows.Forms.dll'),
    (Join-Path $fw 'System.Management.dll'),
    (Join-Path $root 'lib\LibreHardwareMonitorLib.dll')
)
$srcs = @(Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName })
$srcs += (Join-Path $root 'tools\WidgetPreview.cs')

$arguments = @(
    '/nologo', '/target:exe', '/platform:x64', '/codepage:65001',
    '/main:MhiagosControl.WidgetPreview',
    ('/out:' + $out),
    ('/resource:' + (Join-Path $root 'assets\tray.ico')           + ',MhiagosControl.tray.ico'),
    ('/resource:' + (Join-Path $root 'assets\MhiagosControl.ico') + ',MhiagosControl.app.ico'),
    ('/resource:' + (Join-Path $root 'assets\cooler.png')         + ',MhiagosControl.cooler.png'),
    ('/resource:' + (Join-Path $root 'assets\g325.png')           + ',MhiagosControl.g325.png'),
    ('/resource:' + (Join-Path $root 'assets\x11.png')            + ',MhiagosControl.x11.png'),
    ('/resource:' + (Join-Path $root 'assets\bluetooth.png')      + ',MhiagosControl.bluetooth.png')
)
foreach ($r in $refs) { $arguments += ('/reference:' + $r) }
$arguments += $srcs

Get-ChildItem (Join-Path $root 'lib') -Filter *.dll | Where-Object { $_.Name -ne 'api-ms-win-core-sysinfo-825-64.dll' } | Copy-Item -Destination (Join-Path $root 'bin') -Force -ErrorAction SilentlyContinue
& $csc $arguments 2>&1 | Out-String | Write-Output
$rc = $LASTEXITCODE

# O codigo de saida do compilador, e nao a existencia do arquivo, como no build.ps1.
if ($rc -eq 0 -and (Test-Path $out)) { Write-Output ("OK -> " + $out) }
else { Write-Output "FALHOU (csc saiu com $rc)"; exit 1 }
