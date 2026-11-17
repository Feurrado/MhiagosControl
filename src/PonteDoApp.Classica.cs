using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace MhiagosControl
{
    /// <summary>
    /// O que a interface nova precisa para ficar igual a janela classica: os
    /// textos do app (os mesmos T.*, no idioma escolhido), a barra lateral, os
    /// detalhes dos sensores, o Sobre e as acoes que a classica tinha e a ponte
    /// ainda nao (executavel de jogo, diagnostico em arquivo, GitHub).
    /// </summary>
    internal sealed partial class PonteDoApp
    {
        internal const string Repositorio = "https://github.com/Feurrado/MhiagosControl";

        /// <summary>O executavel escolhido que ja tinha vinculo, esperando a confirmacao da pagina.</summary>
        private string _exePendente, _perfilPendente;

        private Dictionary<string, object> Classica(object id, string m, Dictionary<string, object> a)
        {
            switch (m)
            {
                case "textos": return Feito(id, Textos());
                case "barra": return Resultado(id, Barra(a));
                case "visao.classica":
                    {
                        bool ligado = Bool(a, "ligado", false);
                        return Resultado(id, Transacao(delegate { _app.Cfg.VisaoGeralNova = !ligado; return null; }));
                    }
                case "rodizio": return Resultado(id, Rodizio(Texto(a, "nome"), Bool(a, "ligado", false), Int(a, "segundos", -1)));
                case "jogo.adicionar": AdicionarJogo(id, Texto(a, "perfil")); return null;
                case "jogo.substituir": return Resultado(id, ConfirmarJogo(Bool(a, "sim", false)));
                case "diagnostico.salvar": SalvarDiagnostico(id); return null;
                case "github": Navegador.Abrir(Repositorio); return Feito(id, true);
                case "visao.sistema": Sistema(id); return null;
                case "metricas.acao": return AcaoDeMetricas(id, a);
                case "casa.ar": return ComandoDoAr(id, a);
                case "casa.sugestao": AplicarSugestaoDoAr(id); return null;
                case "specs.copiar":
                    {
                        string texto = SpecSheet.EmTexto();
                        if (string.IsNullOrEmpty(texto)) return Falha(id, T.SpecsFailed);
                        try { System.Windows.Forms.Clipboard.SetText(texto); }
                        catch (Exception ex) { Log.Error("copiar especificacoes", ex); return Falha(id, T.CasaFalhaInesperada); }
                        return Feito(id, T.SpecsCopied);
                    }
                case "baterias": return Feito(id, Baterias());
                case "bateria.detalhes": DetalhesDaBateria.Alternar(Texto(a, "chave")); return Feito(id, Baterias());
                default: return Falha(id, "pedido desconhecido");
            }
        }

        // ---------------- textos ----------------

        /// <summary>Todas as propriedades de texto de T, no idioma de agora. A pagina usa os mesmos textos da classica.</summary>
        internal static Dictionary<string, object> Textos()
        {
            Dictionary<string, object> d = D();
            foreach (PropertyInfo p in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Static))
            {
                if (p.PropertyType != typeof(string) || p.GetIndexParameters().Length > 0 || !p.CanRead) continue;
                try { d[p.Name] = (string)p.GetValue(null, null) ?? ""; }
                catch (Exception ex) { Log.Write("interface nova: texto " + p.Name + " (" + ex.GetType().Name + ")"); }
            }
            // Os textos com um nome dentro (Vincular a X, Perfil vinculado: X) vao como molde com {0}.
            foreach (MethodInfo mi in typeof(T).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                ParameterInfo[] ps = mi.GetParameters();
                if (mi.ReturnType != typeof(string) || ps.Length != 1 || ps[0].ParameterType != typeof(string) ||
                    mi.IsGenericMethod || mi.IsSpecialName || d.ContainsKey(mi.Name)) continue;
                try { d[mi.Name] = (string)mi.Invoke(null, new object[] { "{0}" }) ?? ""; }
                catch (Exception ex) { Log.Write("interface nova: molde " + mi.Name + " (" + ex.GetType().Name + ")"); }
            }
            return d;
        }

        // ---------------- o que vai junto no "iniciar" ----------------

        private void AcrescentarClassica(Dictionary<string, object> r)
        {
            Config c = _app.Cfg;
            Dictionary<string, object> barra = D();
            barra["ordem"] = c.NavOrder ?? "";
            barra["recolhida"] = c.SidebarCollapsed;
            r["barra"] = barra;
            r["visaoNova"] = c.VisaoGeralNova;
            r["pasta"] = Paths.DataDir;

            Dictionary<string, object> rtss = D();
            rtss["presente"] = Rtss.Presente();
            rtss["winget"] = Rtss.Winget() != null;
            r["rtss"] = rtss;

            // Detalhes de cada sensor na mesma ordem da lista: tipo (para a unidade do painel 2) e procedencia.
            List<SensorEntry> lista = _app.Sensores();
            List<object> sensores = r["sensores"] as List<object>;
            int hw = 0, lhm = 0;
            if (sensores != null)
            {
                Dictionary<string, SensorEntry> porId = new Dictionary<string, SensorEntry>();
                foreach (SensorEntry e in lista)
                    if (e != null && !string.IsNullOrEmpty(e.Id)) porId[e.Id] = e;
                foreach (object o in sensores)
                {
                    Dictionary<string, object> s = o as Dictionary<string, object>;
                    SensorEntry e;
                    if (s == null || !porId.TryGetValue(Json.Texto(s, "id") ?? "", out e)) continue;
                    s["tipo"] = e.Type.ToString();
                    s["fonte"] = e.Source ?? "";
                    s["grupo"] = T.Category(e.Category ?? "");
                }
            }
            foreach (SensorEntry e in lista)
                if (e != null) { if (e.Source == "HWiNFO") hw++; else lhm++; }

            Dictionary<string, object> sobre = D();
            string painel = HidPanel.UltimoIdentificado;
            sobre["painel"] = string.IsNullOrEmpty(painel) ? "" : painel;
            sobre["testado"] = painel == "VID 1A2C / PID 4984";
            sobre["hw"] = hw; sobre["lhm"] = lhm;
            sobre["fontes"] = T.SensorsFrom("HWiNFO", hw);
            sobre["fontesApoio"] = T.SensorsFrom("LibreHardwareMonitor", lhm);
            sobre["alerta"] = hw == 0 && !PawnIo.Instalado;
            sobre["avisoMotor"] = T.AvisoDoMotor(PawnIo.Instalado);
            sobre["pawnio"] = PawnIo.Instalado;
            sobre["isencao"] = System.Text.RegularExpressions.Regex.Replace(T.Disclaimer, @"(?<!\n)\n(?!\n)", " ");
            sobre["autor"] = T.CreatedBy + "  ·  " + Repositorio.Replace("https://", "");
            sobre["bibliotecas"] = T.LibsNote.Replace("\n", " ");
            sobre["versao"] = T.VersionLabel(AppInfo.Versao);
            r["sobre"] = sobre;

            Dictionary<string, object> rot = D();
            rot["segundos"] = c.RotateSeconds;
            int girando = 0;
            foreach (Profile p in c.Profiles) if (p.Rotate) girando++;
            // A mesma nota da classica: so gira com dois perfis marcados e tempo maior que zero.
            rot["nota"] = girando >= 2 && c.RotateSeconds > 0 ? T.RotationOn(girando) : T.RotationOff;
            r["rodizio"] = rot;
            r["jogosContagem"] = T.GamesBoundCount(c.GameKeys.Count);
            r["destaques"] = Destaques(lista);
        }

        // ---------------- Visao geral ----------------

        /// <summary>
        /// As leituras em destaque da Visao geral: as mesmas da classica (escolha
        /// automatica, nao o conjunto das Metricas), com titulo, procedencia e as
        /// faixas de atencao e perigo para a cor do valor.
        /// </summary>
        private static List<object> Destaques(List<SensorEntry> lista)
        {
            List<object> saida = new List<object>();
            List<string> ids = new List<string>();
            int indice = 0;
            foreach (SensorEntry e in MetricPicker.Destaques(lista))
            {
                Dictionary<string, object> d = D();
                d["id"] = e.Id;
                d["titulo"] = T.DashboardMetricMono(indice, MetricPicker.RotuloCurto(e));
                d["sub"] = SystemInfo.Limpar(e.Hardware) ?? "";
                d["unidade"] = e.EffectiveUnit ?? "";
                float? atencao, perigo;
                MetricPicker.Faixas(e.EffectiveUnit, e.Id, out atencao, out perigo);
                d["atencao"] = atencao.HasValue ? (object)(double)atencao.Value : null;
                d["perigo"] = perigo.HasValue ? (object)(double)perigo.Value : null;
                saida.Add(d);
                ids.Add(e.Id);
                indice++;
            }
            // O historico precisa seguir o que foi posto na tela, como na classica.
            MetricHistory.SeguirTambem(ids);
            return saida;
        }

        /// <summary>A frase do cabecalho, o aviso (se houver) e os ultimos eventos, que mudam a cada segundo.</summary>
        private static void AcrescentarVisao(Dictionary<string, object> d)
        {
            EventosDaVisao.Iniciar();
            string alerta = Visao.Alerta();
            d["alerta"] = alerta != null;
            d["frase"] = alerta ?? T.VisaoTudoEmOrdem(Visao.NomeDoUsuario);
            List<object> eventos = new List<object>();
            foreach (EventosDaVisao.Evento e in EventosDaVisao.Recentes(12))
            {
                Dictionary<string, object> x = D();
                x["h"] = e.Hora.ToString("HH:mm"); x["t"] = e.Texto ?? "";
                eventos.Add(x);
            }
            d["eventos"] = eventos;
            try { d["vcasa"] = VisaoDaCasa(); }
            catch (Exception ex) { Log.Error("interface nova: casa na visao", ex); d["vcasa"] = null; }
        }

        /// <summary>A tira de especificacoes: coletada fora da thread da interface, como as Especificacoes.</summary>
        private void Sistema(object id)
        {
            List<SensorEntry> copia = new List<SensorEntry>(_app.Sensores());
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                Dictionary<string, object> resposta;
                try
                {
                    SystemInfo info = SystemInfo.From(copia);
                    List<object> linhas = new List<object>();
                    AcrescentarLinha(linhas, T.SpecCpu, Junta(info.Cpu, info.CpuNucleos), "cpu");
                    AcrescentarLinha(linhas, T.SpecGpu, Junta(info.Gpu, info.GpuMemoria), "gpu");
                    AcrescentarLinha(linhas, T.SpecRam, info.Ram, "ram");
                    AcrescentarLinha(linhas, T.SpecBoard, info.Placa, "placa");
                    AcrescentarLinha(linhas, T.SpecOs, info.Sistema, "so");
                    resposta = Feito(id, linhas);
                }
                catch (Exception ex)
                {
                    Log.Error("interface nova: sistema", ex);
                    resposta = Falha(id, T.CasaFalhaInesperada);
                }
                Action<Dictionary<string, object>> enviar = Enviar;
                if (enviar != null) enviar(resposta);
            });
        }

        /// <summary>[rotulo, valor, chave]: a chave (cpu, gpu, ram, placa, so) e a rota da secao nas Especificacoes (specs:cpu).</summary>
        private static void AcrescentarLinha(List<object> linhas, string rotulo, string valor, string chave)
        {
            if (string.IsNullOrEmpty(valor)) return;
            List<object> par = new List<object>();
            par.Add(rotulo); par.Add(valor); par.Add(chave);
            linhas.Add(par);
        }

        private static string Junta(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b;
            if (string.IsNullOrEmpty(b)) return a;
            return a + "  ·  " + b;
        }

        // ---------------- barra lateral ----------------

        private string Barra(Dictionary<string, object> a)
        {
            string ordem = Texto(a, "ordem");
            string recolhida = Texto(a, "recolhida");
            return Transacao(delegate
            {
                if (recolhida == "true" || recolhida == "false") _app.Cfg.SidebarCollapsed = recolhida == "true";
                if (ordem != null)
                {
                    // So chaves conhecidas, sem repetir: o resto da ordem e da propria classica.
                    List<string> limpa = new List<string>();
                    foreach (string k in ordem.Split(','))
                    {
                        string t = k.Trim();
                        if (Array.IndexOf(ChavesDaBarra, t) >= 0 && !limpa.Contains(t)) limpa.Add(t);
                    }
                    _app.Cfg.NavOrder = string.Join(",", limpa.ToArray());
                }
                return null;
            });
        }

        internal static readonly string[] ChavesDaBarra = { "visao", "paineis", "metricas", "specs", "config", "baterias", "casa", "sobre" };

        // ---------------- rodizio ----------------

        private string Rodizio(string nome, bool ligado, int segundos)
        {
            Profile p = Achar(nome);
            if (p == null) return T.NovoPerfilInexistente;
            if (segundos != -1 && segundos != 0 && (segundos < 2 || segundos > ConfigTiming.Maximum)) return T.NovoValorInvalido;
            return Transacao(delegate
            {
                p.Rotate = ligado;
                if (segundos >= 0) _app.Cfg.RotateSeconds = segundos;
                return null;
            });
        }

        // ---------------- jogo pelo executavel ----------------

        private void AdicionarJogo(object id, string perfil)
        {
            Depois(id, delegate
            {
                if (Achar(perfil) == null) return Falha(id, T.NovoPerfilInexistente);
                string caminho = PedirArquivo(false, "exe", null);
                if (caminho == null) return Desistiu(id);
                string chave = GameFileBinding.Key(caminho);
                if (_app.Cfg.PerfilDoJogo(chave) != null)
                {
                    // Ja vinculado: a pagina pergunta antes de trocar, como a classica.
                    _exePendente = caminho; _perfilPendente = perfil;
                    Dictionary<string, object> r = Iniciar();
                    r["pergunta"] = T.GameReplace;
                    return Feito(id, r);
                }
                string erro = VincularExe(caminho, perfil);
                if (erro != null) return Falha(id, erro);
                Dictionary<string, object> ok = Iniciar();
                ok["aviso"] = T.GameBound(GameIdentity.Humanize(chave), perfil);
                return Feito(id, ok);
            });
        }

        private string ConfirmarJogo(bool sim)
        {
            string caminho = _exePendente, perfil = _perfilPendente;
            _exePendente = null; _perfilPendente = null;
            if (!sim || caminho == null) return null;
            return VincularExe(caminho, perfil);
        }

        private string VincularExe(string caminho, string perfil)
        {
            if (Achar(perfil) == null) return T.NovoPerfilInexistente;
            return Transacao(delegate { GameFileBinding.Bind(_app.Cfg, caminho, perfil); return null; });
        }

        // ---------------- diagnostico em arquivo ----------------

        private void SalvarDiagnostico(object id)
        {
            Depois(id, delegate
            {
                string texto = Diagnostico();
                string caminho = PedirArquivo(true, "txt", "MhiagosControl-diagnostic-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
                if (caminho == null) return Desistiu(id);
                try { File.WriteAllText(caminho, texto, Encoding.UTF8); }
                catch (Exception ex) { return Falha(id, T.FileFailed + ex.Message); }
                return Feito(id, T.FileSaved);
            });
        }
    }
}
