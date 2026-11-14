using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace MhiagosControl
{
    /// <summary>
    /// O resto do que so a janela classica fazia: perfis (renomear, duplicar,
    /// excluir, padrao, importar, exportar), perfil por jogo, idioma, todos os
    /// sensores, widget, copia de seguranca, RTSS, PawnIO, servidor da Casa e
    /// diagnostico.
    ///
    /// Cada mudanca na configuracao passa por Transacao: copia antes, muda,
    /// grava; se algo falha, a memoria volta a ser o que esta no disco.
    /// </summary>
    internal sealed partial class PonteDoApp
    {
        /// <summary>
        /// Roda depois, fora do evento do WebView2: dialogos de arquivo sao
        /// modais e nao podem abrir dentro dele. Nulo roda na hora (testes).
        /// </summary>
        public Action<Action> Adiar;

        /// <summary>
        /// Pede um arquivo a pessoa: (salvar, tipo "perfil" ou "backup", nome sugerido) -> caminho, ou
        /// nulo se ela desistiu. Nulo aqui quer dizer que nao ha como perguntar.
        /// </summary>
        public Func<bool, string, string, string> Arquivo;

        /// <summary>A copia de seguranca lida e ainda nao aplicada (backup.ler -> backup.aplicar).</summary>
        private Config _restauro;

        private Dictionary<string, object> Ajuste(object id, string m, Dictionary<string, object> a)
        {
            switch (m)
            {
                case "perfil.renomear": return Resultado(id, Renomear(Texto(a, "nome"), Texto(a, "novo")));
                case "perfil.duplicar": return Resultado(id, Duplicar(Texto(a, "nome"), Texto(a, "novo")));
                case "perfil.excluir": return Resultado(id, Excluir(Texto(a, "nome")));
                case "perfil.padrao": return Resultado(id, Padrao(Texto(a, "nome")));
                case "perfil.exportar": Exportar(id, Texto(a, "nome")); return null;
                case "perfil.importar": Importar(id); return null;
                case "jogos.ligar":
                    {
                        bool ligado = Bool(a, "ligado", false);
                        return Resultado(id, Transacao(delegate { _app.Cfg.GameProfiles = ligado; return null; }));
                    }
                case "jogo.vincular": return Resultado(id, Vincular(Texto(a, "perfil")));
                case "jogo.desvincular": return Resultado(id, Desvincular(Texto(a, "chave")));
                case "sensores.todos": return Resultado(id, Todos(Bool(a, "ligado", false)));
                case "idioma": return Resultado(id, Idioma(Texto(a, "lingua")));
                case "widget": return Resultado(id, Widget(a));
                case "widget.canto": _app.WidgetAoCanto(); return Feito(id, true);
                case "backup.exportar": ExportarBackup(id); return null;
                case "backup.ler": LerBackup(id); return null;
                case "backup.aplicar": return Resultado(id, AplicarBackup());
                case "rtss.instalar": return Instalar(id, Rtss.PacoteWinget);
                case "pawnio.instalar": return Instalar(id, PawnIo.PacoteWinget);
                case "rtss.site": Navegador.Abrir(Rtss.Site); return Feito(id, true);
                case "pasta": AbrirPasta(); return Feito(id, true);
                case "diagnostico": return Feito(id, Diagnostico());
                case "casa.servidor": Servidor(id, Texto(a, "externo"), Texto(a, "interno"), Texto(a, "chave")); return null;
                default: return Falha(id, "pedido desconhecido");
            }
        }

        // ---------------- o que vai junto no "iniciar" ----------------

        private void AcrescentarAjustes(Dictionary<string, object> r)
        {
            Config c = _app.Cfg;
            r["todos"] = c.ShowAllSensors;

            Dictionary<string, object> jogos = D();
            jogos["ligado"] = c.GameProfiles;
            jogos["rtss"] = Rtss.Presente();
            List<object> vinculos = new List<object>();
            for (int i = 0; i < c.GameKeys.Count && i < c.GameProfileNames.Count; i++)
            {
                Dictionary<string, object> v = D();
                v["chave"] = c.GameKeys[i];
                v["nome"] = NomeDoJogo(c, c.GameKeys[i]);
                v["perfil"] = c.GameProfileNames[i];
                vinculos.Add(v);
            }
            jogos["vinculos"] = vinculos;
            r["jogos"] = jogos;

            ConfigDoWidget w = _app.Widget;
            if (w != null)
            {
                Dictionary<string, object> wd = D();
                wd["ativo"] = w.Ativo;
                wd["layout"] = w.Layout == LayoutDoWidget.Lista ? "lista" : "aneis";
                wd["vidro"] = w.Nitido ? "nitido" : w.Fosco ? "fosco" : "claro";
                wd["cor"] = w.Cor == CorDoVidro.Escura ? "escura" : w.Cor == CorDoVidro.Clara ? "clara" : "auto";
                wd["frente"] = w.NaFrente; wd["travado"] = w.Travado; wd["casa"] = w.Casa;
                r["widget"] = wd;
            }

            Dictionary<string, object> casa = r["casa"] as Dictionary<string, object>;
            if (casa != null)
            {
                // A chave nunca volta para a pagina: so se ha uma guardada.
                casa["externo"] = ServidorCasa.EnderecoExterno;
                casa["interno"] = ServidorCasa.Endereco;
            }

            Dictionary<string, object> motor = D();
            motor["pawnio"] = PawnIo.Instalado;
            motor["hwinfo"] = HwInfo.Installed;
            r["motor"] = motor;
        }

        private static string NomeDoJogo(Config c, string chave)
        {
            string n = c.NomeDoJogo(chave);
            return string.IsNullOrEmpty(n) ? GameIdentity.Humanize(chave) : n;
        }

        /// <summary>O jogo que o RTSS ve agora e o perfil casado com ele; nulo sem jogo.</summary>
        private Dictionary<string, object> JogoAgora()
        {
            string jogo = Rtss.JogoAtual;
            if (string.IsNullOrEmpty(jogo)) return null;
            Dictionary<string, object> d = D();
            d["chave"] = jogo;
            d["nome"] = NomeDoJogo(_app.Cfg, jogo);
            d["perfil"] = _app.Cfg.PerfilDoJogo(jogo);
            return d;
        }

        // ---------------- transacao ----------------

        /// <summary>Muda e grava; se a mudanca recusar ou a gravacao falhar, nada fica diferente.</summary>
        private string Transacao(Func<string> mudanca)
        {
            Config c = _app.Cfg;
            Config antes = c.Clone();
            string erro;
            try { erro = mudanca(); }
            catch (Exception ex) { Log.Error("interface nova: mudanca", ex); erro = T.CasaFalhaInesperada; }
            if (erro == null) erro = Gravar();
            if (erro != null) c.CopyFrom(antes);
            return erro;
        }

        private static string NomeNovo(Config c, string novo, out string limpo)
        {
            limpo = (novo ?? "").Trim();
            if (!NomeDePerfil.IsMatch(limpo)) return T.NovoNomeInvalido;
            if (c.NameExists(limpo)) return T.NovoNomeRepetido;
            return null;
        }

        // ---------------- perfis ----------------

        private string Renomear(string nome, string novo)
        {
            Profile alvo = Achar(nome);
            if (alvo == null) return T.NovoPerfilInexistente;
            if ((novo ?? "").Trim() == alvo.Name) return null;
            return Transacao(delegate
            {
                Config c = _app.Cfg;
                string limpo = (novo ?? "").Trim();
                // O proprio perfil nao conta como repetido: so a caixa do nome muda.
                string erro = string.Equals(limpo, alvo.Name, StringComparison.OrdinalIgnoreCase)
                    ? (NomeDePerfil.IsMatch(limpo) ? null : T.NovoNomeInvalido)
                    : NomeNovo(c, novo, out limpo);
                if (erro != null) return erro;
                bool eraAtivo = c.ActiveName == alvo.Name;
                bool eraPadrao = c.DefaultProfile.Name == alvo.Name;
                string anterior = alvo.Name;
                alvo.Name = limpo;
                c.RenomearPerfilNosJogos(anterior, limpo);
                if (eraAtivo) c.ActiveName = limpo;
                if (eraPadrao) c.DefaultProfileName = limpo;
                return null;
            });
        }

        private string Duplicar(string nome, string novo)
        {
            Profile origem = Achar(nome);
            if (origem == null) return T.NovoPerfilInexistente;
            return Transacao(delegate
            {
                Config c = _app.Cfg;
                string limpo;
                string erro = NomeNovo(c, novo, out limpo);
                if (erro != null) return erro;
                if (c.Profiles.Count >= 32) return T.NovoValorInvalido;
                Profile copia = origem.Clone();
                copia.Name = limpo;
                c.Profiles.Add(copia);
                return null;
            });
        }

        private string Excluir(string nome)
        {
            if (Achar(nome) == null) return T.NovoPerfilInexistente;
            if (_app.Cfg.Profiles.Count <= 1) return T.KeepOneProfile;
            return Transacao(delegate
            {
                Config c = _app.Cfg;
                Profile alvo = Achar(nome);
                c.RemoverPerfilDosJogos(alvo.Name);
                c.Profiles.Remove(alvo);
                if (c.ActiveName == alvo.Name) c.ActiveName = c.Profiles[0].Name;
                if (c.DefaultProfileName == alvo.Name)
                    c.DefaultProfileName = c.NameExists(c.ActiveName) ? c.ActiveName : c.Profiles[0].Name;
                c.EnsureDefaultProfile();
                return null;
            });
        }

        private string Padrao(string nome)
        {
            Profile p = Achar(nome);
            if (p == null) return T.NovoPerfilInexistente;
            return Transacao(delegate { _app.Cfg.DefaultProfileName = p.Name; return null; });
        }

        // ---------------- trabalho fora do evento ----------------

        private void Depois(object id, Func<Dictionary<string, object>> trabalho)
        {
            Action rodar = delegate
            {
                Dictionary<string, object> r;
                try { r = trabalho(); }
                catch (Exception ex)
                {
                    Log.Error("interface nova: pedido adiado", ex);
                    r = Falha(id, T.CasaFalhaInesperada);
                }
                Action<Dictionary<string, object>> enviar = Enviar;
                if (r != null && enviar != null) enviar(r);
            };
            Action<Action> adiar = Adiar;
            if (adiar != null) adiar(rodar); else rodar();
        }

        private string PedirArquivo(bool salvar, string tipo, string sugerido)
        {
            Func<bool, string, string, string> f = Arquivo;
            return f == null ? null : f(salvar, tipo, sugerido);
        }

        /// <summary>A pessoa desistiu do dialogo: nada mudou, nada a dizer.</summary>
        private static Dictionary<string, object> Desistiu(object id) { return Feito(id, null); }

        private static string NomeDeArquivo(string nome)
        {
            char[] proibidos = System.IO.Path.GetInvalidFileNameChars();
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (char c in nome ?? "") sb.Append(Array.IndexOf(proibidos, c) >= 0 ? '_' : c);
            string s = sb.ToString().Trim();
            return s.Length == 0 ? "perfil" : s;
        }

        private void Exportar(object id, string nome)
        {
            Depois(id, delegate
            {
                Profile p = Achar(nome);
                if (p == null) return Falha(id, T.NovoPerfilInexistente);
                string caminho = PedirArquivo(true, "perfil", NomeDeArquivo(p.Name) + ".ini");
                if (caminho == null) return Desistiu(id);
                string erro;
                if (!Config.ExportProfile(p, caminho, _app.Cfg.SavePath, out erro)) return Falha(id, T.ExportFailed(erro));
                return Feito(id, T.Exported(p.Name));
            });
        }

        private void Importar(object id)
        {
            Depois(id, delegate
            {
                string caminho = PedirArquivo(false, "perfil", null);
                if (caminho == null) return Desistiu(id);
                string erro;
                Profile p = Config.ImportProfile(caminho, out erro);
                if (p == null) return Falha(id, T.ImportFailed(erro));
                erro = Transacao(delegate
                {
                    if (_app.Cfg.Profiles.Count >= 32) return T.NovoValorInvalido;
                    p.Name = _app.Cfg.UniqueName(p.Name);
                    _app.Cfg.Profiles.Add(p);
                    return null;
                });
                if (erro != null) return Falha(id, erro);
                Dictionary<string, object> r = Iniciar();
                // Sensor nao viaja entre maquinas: o perfil de outro PC entra com o mostrador em branco.
                bool conhecidos = SensorConhecido(p.Panel1Id) && SensorConhecido(p.Panel2Id);
                r["aviso"] = conhecidos ? T.Imported(p.Name) : T.ImportedUnknownSensor;
                r["editar"] = p.Name;
                return Feito(id, r);
            });
        }

        private bool SensorConhecido(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            foreach (SensorEntry e in _app.Sensores()) if (e != null && e.Id == id) return true;
            return false;
        }

        // ---------------- jogos ----------------

        private string Vincular(string perfil)
        {
            string jogo = Rtss.JogoAtual;
            if (string.IsNullOrEmpty(jogo)) return T.NoGameToBind;
            Profile p = Achar(perfil);
            if (p == null) return T.NovoPerfilInexistente;
            return Transacao(delegate
            {
                Config c = _app.Cfg;
                c.MapearJogo(jogo, p.Name);
                try
                {
                    GameIdentityInfo info = GameIdentity.Resolve(jogo, Rtss.JogoAtualPid, c.NomeDoJogo(jogo), c.CaminhoDoJogo(jogo));
                    if (info != null) c.IdentificarJogo(jogo, info.DisplayName, info.ExecutablePath);
                }
                catch (Exception ex) { Log.Error("interface nova: identidade do jogo", ex); }
                return null;
            });
        }

        private string Desvincular(string chave)
        {
            if (string.IsNullOrEmpty(chave) || _app.Cfg.PerfilDoJogo(chave) == null) return T.NovoValorInvalido;
            return Transacao(delegate { _app.Cfg.DesmapearJogo(chave); return null; });
        }

        // ---------------- preferencias ----------------

        private string Todos(bool ligado)
        {
            string erro = Transacao(delegate { _app.Cfg.ShowAllSensors = ligado; return null; });
            if (erro == null) _app.TodosOsSensores(ligado);
            return erro;
        }

        private string Idioma(string lingua)
        {
            string alvo = lingua == "en" ? T.EnUs : lingua == "pt" ? T.PtBr : null;
            if (alvo == null) return T.NovoValorInvalido;
            if (alvo == T.Language) return null;
            string erro = Transacao(delegate { _app.Cfg.Language = alvo; return null; });
            if (erro != null) return erro;
            T.Language = alvo;
            _app.IdiomaMudou();
            return null;
        }

        private string Widget(Dictionary<string, object> a)
        {
            ConfigDoWidget w = _app.Widget;
            if (w == null) return T.NovoValorInvalido;
            string layout = Texto(a, "layout"), vidro = Texto(a, "vidro"), cor = Texto(a, "cor");
            if ((layout != null && layout != "aneis" && layout != "lista") ||
                (vidro != null && vidro != "nitido" && vidro != "claro" && vidro != "fosco") ||
                (cor != null && cor != "auto" && cor != "escura" && cor != "clara")) return T.NovoValorInvalido;
            if (Texto(a, "ativo") != null)
            {
                bool ativo = Bool(a, "ativo", w.Ativo);
                if (ativo != w.Ativo) _app.LigarWidget(ativo);
            }
            if (Texto(a, "casa") != null)
            {
                bool casa = Bool(a, "casa", w.Casa);
                if (casa != w.Casa) CasaNoWidget.Definir(casa);
            }
            bool mudou = false;
            if (layout != null) { w.Layout = layout == "lista" ? LayoutDoWidget.Lista : LayoutDoWidget.Aneis; mudou = true; }
            if (vidro != null) { w.Nitido = vidro == "nitido"; w.Fosco = vidro == "fosco"; mudou = true; }
            if (cor != null) { w.Cor = cor == "escura" ? CorDoVidro.Escura : cor == "clara" ? CorDoVidro.Clara : CorDoVidro.Automatica; mudou = true; }
            if (Texto(a, "frente") != null) { w.NaFrente = Bool(a, "frente", w.NaFrente); mudou = true; }
            if (Texto(a, "travado") != null) { w.Travado = Bool(a, "travado", w.Travado); mudou = true; }
            if (mudou) _app.WidgetMudou();
            return null;
        }

        // ---------------- copia de seguranca ----------------

        private const string ExtensaoDoBackup = ".mhiagos-backup";

        private void ExportarBackup(object id)
        {
            Depois(id, delegate
            {
                string caminho = PedirArquivo(true, "backup", "MhiagosControl-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ExtensaoDoBackup);
                if (caminho == null) return Desistiu(id);
                try { ConfigurationBackup.Write(_app.Cfg.Clone(), caminho); }
                catch (Exception ex) { return Falha(id, T.FileFailed + ex.Message); }
                return Feito(id, T.FileSaved);
            });
        }

        private void LerBackup(object id)
        {
            Depois(id, delegate
            {
                string caminho = PedirArquivo(false, "backup", null);
                if (caminho == null) return Desistiu(id);
                Config lido;
                try { lido = ConfigurationBackup.Read(caminho); }
                catch (Exception ex) { return Falha(id, T.FileFailed + ex.Message); }
                _restauro = lido;
                Dictionary<string, object> r = D();
                r["perfis"] = lido.Profiles.Count;
                r["jogos"] = lido.GameKeys.Count;
                r["metricas"] = lido.MetricIds.Count;
                r["ativo"] = lido.ActiveName;
                r["faltando"] = ConfigurationBackup.MissingSensors(lido, _app.Sensores());
                r["nota"] = T.RestoreNote;
                return Feito(id, r);
            });
        }

        /// <summary>Guarda a configuracao de agora ao lado da original e so entao troca pela lida.</summary>
        private string AplicarBackup()
        {
            Config lido = _restauro;
            _restauro = null;
            if (lido == null) return T.NovoValorInvalido;
            Config c = _app.Cfg;
            if (string.IsNullOrEmpty(c.SavePath)) return T.BackupOwnFile;
            try
            {
                string seguranca = c.SavePath + ".before-restore-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") +
                                   "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ExtensaoDoBackup;
                ConfigurationBackup.Write(c, seguranca);
            }
            catch (Exception ex) { return T.FileFailed + ex.Message; }
            string idiomaAntes = T.Language;
            string erro = Transacao(delegate { c.CopyFrom(lido); return null; });
            if (erro != null) return erro;
            _app.TodosOsSensores(c.ShowAllSensors);
            T.Language = string.IsNullOrEmpty(c.Language) ? T.Detect() : c.Language;
            if (T.Language != idiomaAntes) _app.IdiomaMudou();
            return null;
        }

        // ---------------- ferramentas ----------------

        private Dictionary<string, object> Instalar(object id, string pacote)
        {
            string erro = Instalador.Winget(pacote);
            return erro == null ? Feito(id, pacote == Rtss.PacoteWinget ? T.RtssInstalling : T.NovoInstalando) : Falha(id, erro);
        }

        private static void AbrirPasta()
        {
            try { Process.Start("explorer.exe", "\"" + Paths.DataDir + "\""); }
            catch (Exception ex) { Log.Error("interface nova: abrir pasta", ex); }
        }

        private string Diagnostico()
        {
            return SupportReport.Build(_app.Cfg.Clone(), _app.Sensores(), _app.Leituras(), _app.Painel(), Rtss.Presente(), DateTime.UtcNow);
        }

        // ---------------- servidor da Casa ----------------

        /// <summary>Testa no fundo e so grava se ao menos um endereco respondeu com a chave.</summary>
        private void Servidor(object id, string externo, string interno, string chave)
        {
            externo = (externo ?? "").Trim(); interno = (interno ?? "").Trim(); chave = (chave ?? "").Trim();
            if (externo.Length == 0 && interno.Length == 0)
            {
                Action<Dictionary<string, object>> enviar = Enviar;
                if (enviar != null) enviar(Falha(id, T.ServidorEnderecoInvalido));
                return;
            }
            string chaveDoTeste = chave.Length > 0 ? chave : ServidorCasa.ChaveDaPagina;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string erro;
                bool algum = false;
                try { erro = ServidorCasa.TestarEnderecos(externo, interno, chaveDoTeste, out algum); }
                catch (Exception ex) { erro = ex.Message; }
                if (Adiar == null) return;   // a janela fechou durante o teste: nada a gravar daqui
                Depois(id, delegate
                {
                    if (erro != null && !algum) return Falha(id, T.ServidorFalhou(erro));
                    ServidorCasa.Configurar(externo, interno, chave);
                    Dictionary<string, object> r = Iniciar();
                    r["aviso"] = erro == null ? T.NovoServidorSalvo : T.ServidorSalvoComAviso(erro);
                    return Feito(id, r);
                });
            });
        }
    }

    /// <summary>Instala um pacote pelo winget numa janela visivel: ve-se o comando e a resposta, e o Windows ainda pede permissao.</summary>
    internal static class Instalador
    {
        /// <summary>Nulo quando a janela abriu; senao o motivo.</summary>
        public static string Winget(string pacote)
        {
            string winget = Rtss.Winget();
            if (winget == null) return pacote == Rtss.PacoteWinget ? T.RtssNoWinget : T.PawnIoSemWinget;
            try
            {
                string linha = Comando(winget, pacote);
                // O RTSS precisa do runtime antes e, depois, do nosso "iniciar com o Windows" dele.
                if (pacote == Rtss.PacoteWinget)
                    linha = Comando(winget, Rtss.PacoteRuntime) + " & " + linha + " & " +
                            "\"" + System.Windows.Forms.Application.ExecutablePath + "\" " + Rtss.ArgConfigurar;
                ProcessStartInfo psi = new ProcessStartInfo(
                    System.IO.Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/s /k \"" + linha + "\"");
                psi.UseShellExecute = true;
                Process.Start(psi);
                return null;
            }
            catch (Exception ex)
            {
                Log.Error("instalacao pelo winget: " + pacote, ex);
                return T.FileFailed + ex.Message;
            }
        }

        internal static string Comando(string exe, string pacote)
        {
            return "\"" + exe + "\" install --id " + pacote + " -e --source winget --accept-source-agreements";
        }
    }

    public static partial class T
    {
        public static string NovoInstalando { get { return P("A instalação abriu numa janela à parte.", "The installer opened in a separate window."); } }
        public static string NovoServidorSalvo { get { return P("Servidor salvo e respondendo.", "Server saved and responding."); } }
    }
}
