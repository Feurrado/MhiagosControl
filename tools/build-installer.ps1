# Monta o instalador de um arquivo so, com o aplicativo embutido.
#
# ATENCAO: se lib\api-ms-win-core-sysinfo-825-64.dll existir, ela entra DENTRO
# do instalador gerado. Essa biblioteca e comercial (REALiX s.r.o.), licenciada
# ao fabricante do cooler e nao a este projeto. Um instalador assim serve a uso
# pessoal na propria maquina e NAO pode ser publicado, enviado a ninguem nem
# virar Release no GitHub. Para gerar um instalador distribuivel, use -SemMotor.
param([switch]$SemMotor)

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$fw   = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"
$csc  = Join-Path $fw 'csc.exe'
$bin  = Join-Path $root 'bin'
$dist = Join-Path $root 'dist'
$out  = Join-Path $dist 'MhiagosControlSetup.exe'

if (-not (Test-Path $csc)) { Write-Output "ERRO: csc.exe nao encontrado em $fw"; exit 1 }

# O instalador carrega o que estiver em bin\. Compilar antes evita empacotar
# um executavel de duas versoes atras sem perceber.
Write-Output "compilando o aplicativo primeiro..."
& (Join-Path $root 'build.ps1') | Out-String | Write-Output
if ($LASTEXITCODE -ne 0) { Write-Output "FALHOU: o aplicativo nao compilou"; exit 1 }

$app = Join-Path $bin 'MhiagosControl.exe'
$lhm = Join-Path $bin 'LibreHardwareMonitorLib.dll'
$eng = Join-Path $bin 'engine\api-ms-win-core-sysinfo-825-64.dll'

foreach ($f in @($app, $lhm)) {
    if (-not (Test-Path $f)) { Write-Output "ERRO: falta $f"; exit 1 }
}

New-Item -ItemType Directory -Force -Path $dist | Out-Null

$recursos = @(
    ('/resource:' + $app + ',payload.MhiagosControl.exe'),
    ('/resource:' + $lhm + ',payload.LibreHardwareMonitorLib.dll')
)

if ($SemMotor) {
    Write-Output "sem o motor do HWiNFO: instalador distribuivel, e o app sobe na fonte de reserva"
} elseif (Test-Path $eng) {
    $recursos += ('/resource:' + $eng + ',payload.engine.dll')
    Write-Output ""
    Write-Output "  *** o instalador vai conter a biblioteca comercial do HWiNFO ***"
    Write-Output "  uso pessoal nesta maquina. NAO publicar, NAO enviar a ninguem."
    Write-Output ""
} else {
    Write-Output "AVISO: $eng ausente - instalador sem a fonte HWiNFO"
}

$arguments = @(
    '/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/codepage:65001',
    ('/out:' + $out),
    ('/win32manifest:' + (Join-Path $root 'tools\installer.manifest')),
    ('/win32icon:'     + (Join-Path $root 'assets\MhiagosControl.ico')),
    ('/reference:' + (Join-Path $fw 'System.dll')),
    ('/reference:' + (Join-Path $fw 'System.Core.dll')),
    ('/reference:' + (Join-Path $fw 'System.Drawing.dll')),
    ('/reference:' + (Join-Path $fw 'System.Windows.Forms.dll'))
) + $recursos + @((Join-Path $root 'tools\Installer.cs'))

& $csc $arguments 2>&1 | Out-String | Write-Output
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $out)) { Write-Output "FALHOU ao compilar o instalador"; exit 1 }

Write-Output ("OK -> {0} ({1} KB)" -f $out, [math]::Round((Get-Item $out).Length/1KB,1))
