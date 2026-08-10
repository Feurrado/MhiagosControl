# Compila o banco de provas do ciclo de atualizacao. Console, e precisa rodar
# num terminal ADMINISTRADOR: sem privilegio as fontes abrem pela metade.
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$fw   = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"
$csc  = Join-Path $fw 'csc.exe'
$bin  = Join-Path $root 'bin'
$out  = Join-Path $bin 'Bench.exe'

if (-not (Test-Path $csc)) { Write-Output "ERRO: csc.exe nao encontrado em $fw"; exit 1 }
New-Item -ItemType Directory -Force -Path $bin | Out-Null
Copy-Item (Join-Path $root 'lib\LibreHardwareMonitorLib.dll') $bin -Force -ErrorAction SilentlyContinue

$refs = @(
    (Join-Path $fw 'System.dll'),
    (Join-Path $fw 'System.Core.dll'),
    (Join-Path $fw 'System.Drawing.dll'),
    (Join-Path $fw 'System.Windows.Forms.dll'),
    (Join-Path $root 'lib\LibreHardwareMonitorLib.dll')
)

$srcs = @(Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName })
$srcs += (Join-Path $root 'tools\Bench.cs')

$arguments = @(
    '/nologo', '/target:exe', '/platform:x64', '/codepage:65001',
    '/main:MhiagosControl.Bench',
    ('/out:' + $out),
    ('/resource:' + (Join-Path $root 'assets\tray.ico')            + ',MhiagosControl.tray.ico'),
    ('/resource:' + (Join-Path $root 'assets\MhiagosControl.ico')  + ',MhiagosControl.app.ico'),
    ('/resource:' + (Join-Path $root 'assets\cooler.png')          + ',MhiagosControl.cooler.png')
)
foreach ($r in $refs) { $arguments += ('/reference:' + $r) }
$arguments += $srcs

& $csc $arguments 2>&1 | Out-String | Write-Output
if ($LASTEXITCODE -ne 0) { Write-Output "FALHOU ao compilar o banco"; exit 1 }
Write-Output "compilado: $out"
