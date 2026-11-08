using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

namespace MhiagosControl
{
    /// <summary>
    /// Revisao para producao (07/11/2026): o que a revisao achou e a correcao
    /// cobre. Roda dentro de RegressoesAuditoria, com o log desviado para a
    /// pasta temporaria.
    /// </summary>
    public static partial class Tests
    {
        private static void RevisaoDeProducao(string root)
        {
            Secao("revisao para producao: thread da interface, cofre, login e agendamentos");

            // O [STAThread] ficou um tempo no DllImport acima do Main: a interface
            // rodava em MTA e Copiar, Colar e os dialogos de arquivo falhavam.
            MethodInfo main = typeof(Program).GetMethod("Main", BindingFlags.Public | BindingFlags.Static);
            Verdade(main != null && main.GetCustomAttributes(typeof(STAThreadAttribute), false).Length == 1,
                "producao: o Main do aplicativo tem [STAThread]");

            CofreComDpapi(root);
            PoliticaDeLogin();
            DecisaoDosAgendamentos();
            TimerDepoisDaSuspensao(root);
            FreioDoReinicio();
            CenasEntreThreads(root);
            EnderecosSemElevacao();
            VersaoEIsolamento();
            EdicaoPublicaENomes(root);
        }

        private static void EdicaoPublicaENomes(string root)
        {
            List<SensorEntry> semTemperatura = new List<SensorEntry>();
            List<SensorEntry> comTemperatura = new List<SensorEntry>();
            SensorEntry t = new SensorEntry();
            t.Id = "lhm:/cpu/0/temperature/0"; t.Label = "CPU Package"; t.Type = LibreHardwareMonitor.Hardware.SensorType.Temperature;
            comTemperatura.Add(t);
            Verdade(TrayContext.FaltaPawnIo(false, false, semTemperatura), "pawnio: sem HWiNFO, sem PawnIO e sem temperatura da CPU, avisa");
            Verdade(!TrayContext.FaltaPawnIo(true, false, semTemperatura), "pawnio: com a biblioteca do HWiNFO, nao avisa");
            Verdade(!TrayContext.FaltaPawnIo(false, true, semTemperatura), "pawnio: com o PawnIO instalado, nao avisa");
            Verdade(!TrayContext.FaltaPawnIo(false, false, comTemperatura), "pawnio: com a temperatura da CPU lida, nao avisa");
            Verdade(T.AvisoDoMotor(false).Contains("PawnIO") && T.AvisoDoMotor(true).Contains("PawnIO") &&
                    T.AvisoDoMotor(false) != T.AvisoDoMotor(true), "pawnio: aviso do Sobre diferente com e sem o PawnIO");
            Verdade(T.AvisoDoMotor(false).IndexOf("instalador oficial", StringComparison.OrdinalIgnoreCase) < 0,
                "pawnio: o aviso nao manda mais reinstalar pelo instalador oficial (que nao traz a biblioteca)");

            using (Bitmap bmp = new Bitmap(10, 10))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                string apoio = "100% → 41%  ·  " + T.SinceTracking("06/11, 14:00");
                Verdade(CartaoDeBateria.CabeEmDuasLinhas(g, apoio, Ui.S(150)), "baterias: a linha de apoio do bloco cabe em duas linhas");
                Verdade(!CartaoDeBateria.CabeEmDuasLinhas(g, apoio + " " + apoio + " " + apoio, Ui.S(150)),
                    "baterias: texto que nao cabe em duas linhas e detectado (vira uma linha com reticencias)");
            }

            string antesArquivo = Visao.ArquivoDoNome;
            object antesNome = typeof(Visao).GetField("_nome", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            Visao.ArquivoDoNome = Path.Combine(root, "nome.txt");
            try
            {
                Verdade(Visao.NomeGuardado() == null, "saudacao: sem nome guardado, nenhum (nada de login cortado)");
                Visao.NomeAchado("Thiago");
                Igual("Thiago", Visao.NomeGuardado(), "saudacao: o nome achado fica guardado para a proxima abertura");
                Igual("Thiago", Visao.PrimeiroNome("thiago moreira"), "saudacao: primeiro nome com maiuscula");
            }
            finally
            {
                Visao.ArquivoDoNome = antesArquivo;
                typeof(Visao).GetField("_nome", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, antesNome);
            }
        }

        private static void VersaoEIsolamento()
        {
            Igual(AppInfo.Versao, MhiagosSetup.Setup.Versao, "versao: instalador e app dizem a mesma");
            Igual(AppInfo.Versao + ".0", typeof(Program).Assembly.GetName().Version.ToString(),
                "versao: o executavel leva a versao do app no recurso");
            Verdade(!Log.Path.StartsWith(Paths.DataDir, StringComparison.OrdinalIgnoreCase),
                "testes: o log nao vai para o log.txt de quem usa o app");
        }

        private static void EnderecosSemElevacao()
        {
            Uri u;
            Verdade(Navegador.Seguro("http://192.168.1.100:8765", out u) && Navegador.Seguro("https://github.com/x", out u),
                "navegador: http e https com host passam");
            Verdade(!Navegador.Seguro("file:///C:/Windows/System32/calc.exe", out u), "navegador: file: e recusado");
            Verdade(!Navegador.Seguro("ms-msdt:/id x", out u) && !Navegador.Seguro("search-ms:query=x", out u),
                "navegador: esquemas do Windows sao recusados");
            Verdade(!Navegador.Seguro("C:\\Users\\Public\\x.exe", out u) && !Navegador.Seguro("", out u) && !Navegador.Seguro(null, out u),
                "navegador: caminho de programa e vazio sao recusados");
            Verdade(!Navegador.Seguro("http://usuario:senha@192.168.1.100/", out u), "navegador: endereco com usuario e senha e recusado");

            List<string> abertos = new List<string>();
            Navegador.Interceptar = delegate(string url) { abertos.Add(url); };
            try
            {
                Verdade(Navegador.Abrir("https://github.com/Feurrado/MhiagosControl") && abertos.Count == 1,
                    "navegador: endereco bom abre");
                Verdade(!Navegador.Abrir("file:///C:/x.exe") && abertos.Count == 1, "navegador: endereco ruim nao chega a abrir");
            }
            finally { Navegador.Interceptar = null; }

            bool antes = ServidorCasa.Desligado;
            try
            {
                Igual(T.ServidorEnderecoInvalido, ServidorCasa.Testar("file:///C:/Windows/win.ini", "chave"),
                    "servidor da casa: endereco file: nem chega a virar pedido");
                string dois = ServidorCasa.TestarEnderecos("file:///C:/a", "file:///C:/b", "chave");
                Verdade(dois != null && dois.Contains(T.ServidorEnderecoExterno) && dois.Contains(T.ServidorEnderecoInterno),
                    "servidor da casa: com dois enderecos ruins o erro diz qual e qual");
                Verdade(ServidorCasa.TestarEnderecos("", "", "chave") == null, "servidor da casa: sem endereco nao ha o que testar");
            }
            finally { ServidorCasa.Desligado = antes; }
        }

        private static void CofreComDpapi(string root)
        {
            string pasta = Path.Combine(root, "cofre");
            Directory.CreateDirectory(pasta);
            string antesPasta = Cofre.Pasta;
            bool antesSem = Cofre.SemGerenciador;
            Cofre.Pasta = pasta;
            Cofre.SemGerenciador = true;
            try
            {
                const string Alvo = "MhiagosControl/Teste";
                Verdade(Cofre.Ler(Alvo) == null, "cofre: alvo sem arquivo volta nulo");
                Verdade(Cofre.Gravar(Alvo, "segredo-çã-123"), "cofre: grava com a DPAPI");
                Igual("segredo-çã-123", Cofre.Ler(Alvo), "cofre: le de volta o mesmo texto (acentos inclusos)");
                string bruto = Encoding.UTF8.GetString(File.ReadAllBytes(Cofre.Arquivo(Alvo)));
                Verdade(bruto.IndexOf("segredo", StringComparison.Ordinal) < 0, "cofre: o arquivo nao guarda o texto claro");

                // A entropia leva o alvo: o arquivo de um nao abre como o de outro.
                File.Copy(Cofre.Arquivo(Alvo), Cofre.Arquivo("MhiagosControl/Outro"), true);
                Verdade(Cofre.Ler("MhiagosControl/Outro") == null, "cofre: arquivo copiado para outro alvo nao abre");

                Cofre.Apagar(Alvo);
                Verdade(Cofre.Ler(Alvo) == null && !File.Exists(Cofre.Arquivo(Alvo)), "cofre: apagar tira o arquivo");

                string r, s;
                Verdade(CofreConnectLife.Gravar("rt-1", "src-1"), "cofre: grava o acesso da ConnectLife");
                Verdade(CofreConnectLife.Ler(out r, out s) && r == "rt-1" && s == "src-1", "cofre: le o acesso da ConnectLife de volta");
                Verdade(CofreConnectLife.Gravar("rt-2", "src-1") && CofreConnectLife.Ler(out r, out s) && r == "rt-2",
                    "cofre: o token renovado substitui o anterior");
                CofreConnectLife.Apagar();
                Verdade(!CofreConnectLife.Ler(out r, out s), "cofre: sair apaga o acesso");
            }
            finally { Cofre.Pasta = antesPasta; Cofre.SemGerenciador = antesSem; }
        }

        private static void PoliticaDeLogin()
        {
            Verdade(ConnectLifeNuvem.PedeLoginNoToken(400) && ConnectLifeNuvem.PedeLoginNoToken(401),
                "login: 400 e 401 do token (invalid_grant) pedem login");
            Verdade(!ConnectLifeNuvem.PedeLoginNoToken(403) && !ConnectLifeNuvem.PedeLoginNoToken(408) &&
                    !ConnectLifeNuvem.PedeLoginNoToken(429) && !ConnectLifeNuvem.PedeLoginNoToken(503),
                "login: 403, 408, 429 e 5xx do token sao temporarios");
            Verdade(ConnectLifeNuvem.PedeLoginNaApi(401) && !ConnectLifeNuvem.PedeLoginNaApi(403) && !ConnectLifeNuvem.PedeLoginNaApi(429),
                "login: na API so o 401 pede login");

            DateTime t0 = new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);
            DateTime primeira = DateTime.MinValue;
            Verdade(!ServicoCasa.EsquecerConta(ref primeira, t0) && primeira == t0, "login: a primeira recusa so marca");
            Verdade(!ServicoCasa.EsquecerConta(ref primeira, t0.AddSeconds(30)), "login: segunda recusa logo em seguida nao esquece a conta");
            Verdade(ServicoCasa.EsquecerConta(ref primeira, t0.AddSeconds(ServicoCasa.SegundosEntreRecusas + 1)) && primeira == DateTime.MinValue,
                "login: segunda recusa depois de 1 min esquece a conta");
        }

        private static void DecisaoDosAgendamentos()
        {
            DateTime t0 = new DateTime(2026, 11, 7, 23, 0, 0, DateTimeKind.Utc);
            long alvo = t0.Ticks;
            Igual(ServicoCasa.Decisao.PcManda, ServicoCasa.DecidirAgendamento(alvo, t0.AddMinutes(1), null),
                "agendamento: sem servidor, na hora, o PC manda");
            Igual(ServicoCasa.Decisao.Descartar, ServicoCasa.DecidirAgendamento(alvo, t0.AddMinutes(16), null),
                "agendamento: sem servidor, 16 min atrasado (PC suspenso), descarta");
            Igual(ServicoCasa.Decisao.ServidorFez, ServicoCasa.DecidirAgendamento(alvo, t0.AddHours(8), ServidorCasa.Feito),
                "agendamento: o servidor fez na hora; o PC so avisa, mesmo voltando horas depois");
            Igual(ServicoCasa.Decisao.Esperar, ServicoCasa.DecidirAgendamento(alvo, t0.AddMinutes(1), ServidorCasa.Pendente),
                "agendamento: servidor ainda tentando, o PC espera");
            Igual(ServicoCasa.Decisao.PcManda, ServicoCasa.DecidirAgendamento(alvo, t0.AddMinutes(3), ServidorCasa.Pendente),
                "agendamento: servidor demorando demais, o PC assume");
            Igual(ServicoCasa.Decisao.Descartar, ServicoCasa.DecidirAgendamento(alvo, t0.AddMinutes(20), ServidorCasa.Pendente),
                "agendamento: pendente e muito atrasado, descarta");
            Igual(ServicoCasa.Decisao.PcManda, ServicoCasa.DecidirAgendamento(alvo, t0.AddMinutes(5), ServidorCasa.Falhou),
                "agendamento: o servidor desistiu, o PC manda");
            Igual(ServicoCasa.Decisao.Cancelado, ServicoCasa.DecidirAgendamento(alvo, t0.AddMinutes(1), ServidorCasa.Cancelado),
                "agendamento: cancelado pelo painel web, nada a fazer");
            Igual(ServicoCasa.Decisao.PcManda, ServicoCasa.DecidirAgendamento(alvo, t0.AddMinutes(1), ServidorCasa.Indisponivel),
                "agendamento: servidor fora do ar, o PC manda");
        }

        private static void TimerDepoisDaSuspensao(string root)
        {
            string prefAntes = PreferenciasCasa.Arquivo;
            PreferenciasCasa prefOriginal = ServicoCasa.Pref;
            Action<string, string> notificarAntes = ServicoCasa.Notificar;
            List<string> avisos = new List<string>();
            PreferenciasCasa.Arquivo = Path.Combine(root, "casa-producao.ini");
            ServicoCasa.Pref = new PreferenciasCasa();
            ServicoCasa.Notificar = delegate(string titulo, string texto) { lock (avisos) avisos.Add(texto); };
            MethodInfo conferir = typeof(ServicoCasa).GetMethod("ConferirTimer", BindingFlags.NonPublic | BindingFlags.Static);
            try
            {
                ServicoCasa.Simular(Exemplo());

                ServicoCasa.Pref.DesligarEmUtc = DateTime.UtcNow.AddMinutes(-20).Ticks;
                ServicoCasa.Pref.DesligarPuid = "p1";
                conferir.Invoke(null, null);
                Verdade(ServicoCasa.Pref.DesligarEmUtc == 0, "timer: vencido com o PC suspenso sai da fila");
                Verdade(avisos.Contains(T.CasaAgendamentoAtrasado) && !avisos.Contains(T.CasaTimerDesligou),
                    "timer: vencido ha 20 min nao desliga o ar e avisa por que");

                avisos.Clear();
                ServicoCasa.Pref.DesligarEmUtc = DateTime.UtcNow.AddMinutes(-1).Ticks;
                ServicoCasa.Pref.DesligarPuid = "p1";
                conferir.Invoke(null, null);
                Verdade(ServicoCasa.Pref.DesligarEmUtc == 0 && avisos.Contains(T.CasaTimerDesligou),
                    "timer: vencido ha 1 min ainda desliga");

                avisos.Clear();
                conferir.Invoke(null, null);
                Verdade(avisos.Count == 0, "timer: conferir de novo (relogio e leitura juntos) nao manda duas vezes");
            }
            finally
            {
                ServicoCasa.PararSimulacao();
                ServicoCasa.Pref = prefOriginal;
                ServicoCasa.Notificar = notificarAntes;
                PreferenciasCasa.Arquivo = prefAntes;
            }
        }

        private static void FreioDoReinicio()
        {
            DateTime t0 = new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);
            List<DateTime> l = new List<DateTime> { t0.AddMinutes(-9), t0.AddMinutes(-5) };
            Verdade(Reinicio.PodeReiniciar(l, t0), "reinicio: dois em 10 min ainda deixa reabrir");
            l.Add(t0.AddMinutes(-1));
            Verdade(!Reinicio.PodeReiniciar(l, t0), "reinicio: tres em 10 min param (sem laco de queda)");
            Verdade(Reinicio.PodeReiniciar(new List<DateTime> { t0.AddMinutes(-30), t0.AddMinutes(-20), t0.AddMinutes(-11) }, t0),
                "reinicio: quedas antigas nao contam");
        }

        private static void CenasEntreThreads(string root)
        {
            string prefAntes = PreferenciasCasa.Arquivo;
            PreferenciasCasa.Arquivo = Path.Combine(root, "casa-cenas.ini");
            try
            {
                PreferenciasCasa p = new PreferenciasCasa();
                Exception erro = null;
                ManualResetEvent parar = new ManualResetEvent(false);
                Thread gravador = new Thread(delegate()
                {
                    try { while (!parar.WaitOne(0)) p.Salvar(); }
                    catch (Exception ex) { erro = ex; }
                });
                gravador.Start();
                Stopwatch sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 300)
                {
                    List<CenaCasa> nova = new List<CenaCasa>(p.Cenas);
                    nova.Reverse();
                    p.Cenas = nova;
                }
                parar.Set();
                gravador.Join();
                Verdade(erro == null, "cenas: gravar o casa.ini enquanto as cenas mudam em outra thread nao quebra");
                Verdade(PreferenciasCasa.Carregar().Cenas.Count == p.Cenas.Count, "cenas: o arquivo gravado volta com todas as cenas");
                p.Servidor = "192.168.1.100:8765"; p.ServidorExterno = "https://home01.exemplo.ts.net";
                p.Salvar();
                PreferenciasCasa lidoDoDisco = PreferenciasCasa.Carregar();
                Verdade(lidoDoDisco.Servidor == p.Servidor && lidoDoDisco.ServidorExterno == p.ServidorExterno,
                    "servidor: endereco externo e interno voltam do casa.ini");
            }
            finally { PreferenciasCasa.Arquivo = prefAntes; }
        }
    }
}
