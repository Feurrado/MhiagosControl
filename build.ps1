# Compila o Mhiagos Control com o csc.exe do .NET Framework (nao exige SDK).
param([switch]$Assets)

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$fw   = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"
$csc  = Join-Path $fw 'csc.exe'
$bin  = Join-Path $root 'bin'
$out  = Join-Path $bin 'MhiagosControl.exe'

if (-not (Test-Path $csc)) { Write-Output "ERRO: csc.exe nao encontrado em $fw"; exit 1 }
New-Item -ItemType Directory -Force -Path $bin | Out-Null

# regenera icones e imagem do cooler a partir dos PNGs originais
if ($Assets) {
    $mk = Join-Path $root 'tools\MakeAssets.exe'
    if (-not (Test-Path $mk)) {
        & $csc /nologo /target:exe /platform:x64 ("/out:" + $mk) `
            ("/reference:" + (Join-Path $fw 'System.dll')) `
            ("/reference:" + (Join-Path $fw 'System.Drawing.dll')) `
            (Join-Path $root 'tools\MakeAssets.cs')
    }
    & $mk (Join-Path $root 'assets') (Join-Path $root 'assets')
}

# a biblioteca de sensores precisa ficar ao lado do executavel
Copy-Item (Join-Path $root 'lib\LibreHardwareMonitorLib.dll') $bin -Force

# motor do HWiNFO: cobre temperatura, potencia e clock real da CPU, que a
# LibreHardwareMonitor nao consegue ler porque seu driver esta bloqueado.
# Fica em engine\ para deixar claro que e uma dependencia externa opcional -
# sem ela o aplicativo continua funcionando com os demais sensores.
$engine = Join-Path $bin 'engine'
New-Item -ItemType Directory -Force -Path $engine | Out-Null
$hw = Join-Path $root 'lib\api-ms-win-core-sysinfo-825-64.dll'
if (Test-Path $hw) { Copy-Item $hw $engine -Force }
else { Write-Output "AVISO: $hw ausente - o app subira sem a fonte HWiNFO" }

$refs = @(
    (Join-Path $fw 'System.dll'),
    (Join-Path $fw 'System.Core.dll'),
    (Join-Path $fw 'System.Drawing.dll'),
    (Join-Path $fw 'System.Windows.Forms.dll'),
    (Join-Path $root 'lib\LibreHardwareMonitorLib.dll')
)
$srcs = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }

$arguments = @(
    '/nologo', '/target:winexe', '/platform:x64', '/optimize+',
    # le os fontes como UTF-8 mesmo sem BOM; sem isso o build depende
    # da pagina de codigo da maquina e acentos viram lixo
    '/codepage:65001',
    ('/out:' + $out),
    ('/win32manifest:' + (Join-Path $root 'app.manifest')),
    ('/win32icon:'     + (Join-Path $root 'assets\MhiagosControl.ico')),
    ('/resource:' + (Join-Path $root 'assets\tray.ico')            + ',MhiagosControl.tray.ico'),
    ('/resource:' + (Join-Path $root 'assets\MhiagosControl.ico')  + ',MhiagosControl.app.ico'),
    ('/resource:' + (Join-Path $root 'assets\cooler.png')          + ',MhiagosControl.cooler.png')
)
foreach ($r in $refs) { $arguments += ('/reference:' + $r) }
$arguments += $srcs

Write-Output "compilando $($srcs.Count) fontes..."
& $csc $arguments 2>&1 | Out-String | Write-Output

if (Test-Path $out) {
    Write-Output ("OK -> {0} ({1} KB)" -f $out, [math]::Round((Get-Item $out).Length/1KB,1))
} else {
    Write-Output "FALHOU"
    exit 1
}
