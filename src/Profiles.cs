using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MhiagosControl
{
    /// <summary>
    /// Um conjunto nomeado de configuracao. O hardware tem dois mostradores;
    /// perfis permitem trocar o que vai neles sem reabrir a janela.
    /// </summary>
    public class Profile
    {
        public string Name = "Padrao";
        public string Panel1Id = "";
        public string Panel2Id = "";
        public bool Fahrenheit = false;
        public bool Percent = true;

        /// <summary>
        /// Divisor aplicado antes de enviar (1, 10, 100, 1000). Zero significa
        /// "automatico": usa a sugestao para o tipo do sensor.
        /// </summary>
        public int Divisor1 = 0;
        public int Divisor2 = 0;

        /// <summary>
        /// Entra na roda do rodizio.
        ///
        /// O rodizio troca perfis inteiros, e nao sensores dentro de um
        /// mostrador, porque o indicador de unidade e do quadro: °C/°F em cima
        /// e %/W embaixo valem para o quadro todo. Girar so o sensor poria
        /// watts sob o indicador de porcentagem, e o mostrador mentiria sem
        /// jeito de perceber.
        /// </summary>
        public bool Rotate = false;

        public Profile Clone()
        {
            Profile p = new Profile();
            p.Name = Name; p.Panel1Id = Panel1Id; p.Panel2Id = Panel2Id;
            p.Fahrenheit = Fahrenheit; p.Percent = Percent;
            p.Divisor1 = Divisor1; p.Divisor2 = Divisor2;
            p.Rotate = Rotate;
            return p;
        }

        public override string ToString() { return Name; }
    }

    /// <summary>Uma grade nomeada da aba Métricas.</summary>
    public sealed class MetricProfile
    {
        public string Name = "";
        public int Range = MetricHistory.JanelaPadrao;
        public List<string> Ids = new List<string>();
        public List<int> Sizes = new List<int>();

        public MetricProfile Clone()
        {
            MetricProfile copy = new MetricProfile();
            copy.Name = Name;
            copy.Range = Range;
            copy.Ids.AddRange(Ids);
            copy.Sizes.AddRange(Sizes);
            return copy;
        }

        public int Size(int index)
        {
            return index >= 0 && index < Sizes.Count ? Sizes[index] : 0;
        }
    }

    /// <summary>
    /// Configuracao completa, em INI seccionado.
    /// Grava em %LOCALAPPDATA%\MhiagosControl\config.ini
    /// </summary>
    public class Config
    {
        // O destino pertence a instancia carregada. Uma Config criada apenas
        // para previa, teste ou importacao nao pode cair silenciosamente no
        // arquivo real do usuario ao chamar Save().
        private string _savePath;

        public List<Profile> Profiles = new List<Profile>();
        public string ActiveName = "Padrao";

        /// <summary>
        /// Perfil de repouso. É para ele que o mostrador volta quando termina
        /// um jogo vinculado; não depende do perfil que estava ativo antes.
        /// </summary>
        public string DefaultProfileName = "";

        /// <summary>
        /// Falso resume os sensores por nucleo em medias. Preferencia global,
        /// nao do perfil: e sobre o que a lista mostra, nao sobre o painel.
        /// </summary>
        public bool ShowAllSensors = false;

        /// <summary>
        /// Idioma da interface. Vazio significa "nunca escolhido": nesse caso
        /// o idioma do Windows decide, e so vira valor gravado quando o usuario
        /// escolher - assim mudar a lingua do sistema ainda acompanha, ate que
        /// alguem diga o contrario.
        /// </summary>
        public string Language = "";

        /// <summary>
        /// Minutos sem teclado nem mouse ate apagar o mostrador; 0 desliga.
        ///
        /// Preferencia global e nao do perfil: e sobre a maquina estar em uso,
        /// nao sobre o que o mostrador exibe quando esta em uso.
        /// </summary>
        public int IdleBlankMinutes = 0;

        /// <summary>Segundos em cada perfil do rodizio; 0 desliga.</summary>
        public int RotateSeconds = 0;

        /// <summary>
        /// Barra lateral recolhida na faixa de icones.
        ///
        /// Guardado porque e escolha de espaco de tela, nao acao pontual: quem
        /// recolheu quis mais area para o conteudo, e reabrir a janela na
        /// largura cheia desfaria isso a cada vez.
        /// </summary>
        public bool SidebarCollapsed = false;

        /// <summary>
        /// Cartoes da aba Metricas, na ordem em que aparecem.
        ///
        /// Vazio significa "ainda nao escolhido", e nao "nenhum": a primeira
        /// abertura monta uma selecao automatica e grava. Distinguir os dois
        /// importa - quem remover todos os cartoes de proposito nao quer ve-los
        /// voltarem no proximo arranque, e por isso a lista gravada vazia usa um
        /// marcador em vez de simplesmente sumir do arquivo.
        /// </summary>
        public List<string> MetricIds = new List<string>();

        /// <summary>Tamanho de cada cartao: 0 pequeno, 1 medio, 2 grande.</summary>
        public List<int> MetricSizes = new List<int>();

        public bool MetricsChosen = false;

        /// <summary>Janela de tempo dos graficos, em segundos.</summary>
        public int MetricRange = MetricHistory.JanelaPadrao;

        /// <summary>Grades de métricas salvas pelo usuário.</summary>
        public List<MetricProfile> MetricProfiles = new List<MetricProfile>();

        /// <summary>
        /// Tamanho da janela de configuracao, em pixels de area util.
        ///
        /// Zero significa "nunca foi ajustada" e vale o padrao. Redimensionar sem
        /// lembrar seria trabalho refeito a cada abertura, que e a metade chata
        /// de uma janela redimensionavel.
        /// </summary>
        public int WindowW = 0, WindowH = 0;

        /// <summary>
        /// Trocar de perfil sozinho quando um jogo abre.
        ///
        /// Desligado por padrao, e nao e timidez: isto mexe no que aparece na
        /// peca sem ninguem pedir. Um recurso que age sozinho tem de ser
        /// escolhido, nao descoberto.
        /// </summary>
        public bool GameProfiles = false;

        /// <summary>
        /// Mapa executavel -> nome do perfil, em duas listas paralelas.
        ///
        /// Casa pelo EXECUTAVEL e nao pelo titulo da janela: o titulo muda com
        /// patch, idioma e placar, e um casamento que quebra quando o jogo
        /// atualiza quebra justamente quando ninguem esta olhando para isso.
        /// </summary>
        public List<string> GameKeys = new List<string>();
        public List<string> GameProfileNames = new List<string>();
        public List<string> GameDisplayNames = new List<string>();
        public List<string> GamePaths = new List<string>();

        /// <summary>
        /// Copia profunda usada por editores e operacoes transacionais. Listas e
        /// perfis nunca sao compartilhados entre a copia publicada e o rascunho.
        /// </summary>
        public Config Clone()
        {
            Config c = new Config();
            c._savePath = _savePath;
            c.CopyFrom(this);
            return c;
        }

        /// <summary>Substitui todo o estado mantendo a identidade desta instancia.</summary>
        public void CopyFrom(Config other)
        {
            if (other == null) throw new ArgumentNullException("other");

            Profiles.Clear();
            foreach (Profile p in other.Profiles) Profiles.Add(p.Clone());
            ActiveName = other.ActiveName;
            DefaultProfileName = other.DefaultProfileName;
            ShowAllSensors = other.ShowAllSensors;
            Language = other.Language;
            IdleBlankMinutes = other.IdleBlankMinutes;
            RotateSeconds = other.RotateSeconds;
            SidebarCollapsed = other.SidebarCollapsed;
            MetricsChosen = other.MetricsChosen;
            MetricRange = other.MetricRange;
            WindowW = other.WindowW;
            WindowH = other.WindowH;
            GameProfiles = other.GameProfiles;

            MetricIds.Clear();
            MetricIds.AddRange(other.MetricIds);
            MetricSizes.Clear();
            MetricSizes.AddRange(other.MetricSizes);
            MetricProfiles.Clear();
            foreach (MetricProfile p in other.MetricProfiles) MetricProfiles.Add(p.Clone());
            GameKeys.Clear();
            GameKeys.AddRange(other.GameKeys);
            GameProfileNames.Clear();
            GameProfileNames.AddRange(other.GameProfileNames);
            GameDisplayNames.Clear();
            GameDisplayNames.AddRange(other.GameDisplayNames);
            GamePaths.Clear();
            GamePaths.AddRange(other.GamePaths);
        }

        /// <summary>Perfil casado com o executavel, ou nulo.</summary>
        public string PerfilDoJogo(string exe)
        {
            if (string.IsNullOrEmpty(exe)) return null;
            for (int i = 0; i < GameKeys.Count && i < GameProfileNames.Count; i++)
                if (string.Equals(GameKeys[i], exe, StringComparison.OrdinalIgnoreCase))
                    return GameProfileNames[i];
            return null;
        }

        /// <summary>Casa um executavel com um perfil, substituindo o casamento anterior.</summary>
        public void MapearJogo(string exe, string perfil)
        {
            if (string.IsNullOrEmpty(exe) || string.IsNullOrEmpty(perfil)) return;
            DesmapearJogo(exe);
            GameKeys.Add(exe.Trim());
            GameProfileNames.Add(perfil.Trim());
            GameDisplayNames.Add("");
            GamePaths.Add("");
        }

        public string NomeDoJogo(string exe)
        {
            int i = IndiceDoJogo(exe);
            return i >= 0 && i < GameDisplayNames.Count ? GameDisplayNames[i] : null;
        }

        public string CaminhoDoJogo(string exe)
        {
            int i = IndiceDoJogo(exe);
            return i >= 0 && i < GamePaths.Count ? GamePaths[i] : null;
        }

        /// <summary>Guarda a identidade visual sem trocar o perfil associado.</summary>
        public bool IdentificarJogo(string exe, string nome, string caminho)
        {
            int i = IndiceDoJogo(exe);
            if (i < 0) return false;
            while (GameDisplayNames.Count <= i) GameDisplayNames.Add("");
            while (GamePaths.Count <= i) GamePaths.Add("");

            nome = nome == null ? "" : nome.Trim();
            caminho = caminho == null ? "" : caminho.Trim();
            if (GameDisplayNames[i] == nome && GamePaths[i] == caminho) return false;
            GameDisplayNames[i] = nome;
            GamePaths[i] = caminho;
            return true;
        }

        private int IndiceDoJogo(string exe)
        {
            if (string.IsNullOrEmpty(exe)) return -1;
            for (int i = 0; i < GameKeys.Count; i++)
                if (string.Equals(GameKeys[i], exe, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        public void DesmapearJogo(string exe)
        {
            if (string.IsNullOrEmpty(exe)) return;
            for (int i = GameKeys.Count - 1; i >= 0; i--)
                if (string.Equals(GameKeys[i], exe, StringComparison.OrdinalIgnoreCase))
                {
                    GameKeys.RemoveAt(i);
                    if (i < GameProfileNames.Count) GameProfileNames.RemoveAt(i);
                    if (i < GameDisplayNames.Count) GameDisplayNames.RemoveAt(i);
                    if (i < GamePaths.Count) GamePaths.RemoveAt(i);
                }
        }

        public void RenomearPerfilNosJogos(string anterior, string novo)
        {
            for (int i = 0; i < GameProfileNames.Count; i++)
                if (string.Equals(GameProfileNames[i], anterior, StringComparison.Ordinal))
                    GameProfileNames[i] = novo;
        }

        public void RemoverPerfilDosJogos(string perfil)
        {
            for (int i = GameProfileNames.Count - 1; i >= 0; i--)
                if (string.Equals(GameProfileNames[i], perfil, StringComparison.Ordinal) && i < GameKeys.Count)
                    DesmapearJogo(GameKeys[i]);
        }

        /// <summary>Tamanho do cartao i, tolerando lista curta de versao antiga.</summary>
        public int MetricSize(int i)
        {
            return (i >= 0 && i < MetricSizes.Count) ? MetricSizes[i] : 0;
        }

        public MetricProfile MetricProfileByName(string name)
        {
            foreach (MetricProfile profile in MetricProfiles)
                if (string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase))
                    return profile;
            return null;
        }

        public bool MetricProfileNameExists(string name)
        {
            return MetricProfileByName(name) != null;
        }

        /// <summary>Perfis marcados para o rodizio. Menos de dois nao e rodizio.</summary>
        public List<Profile> Rotation
        {
            get
            {
                List<Profile> r = new List<Profile>();
                foreach (Profile p in Profiles) if (p.Rotate) r.Add(p);
                return r;
            }
        }

        private static string FilePath
        {
            get { return Path.Combine(Paths.DataDir, "config.ini"); }
        }

        public Profile Active
        {
            get
            {
                foreach (Profile p in Profiles)
                    if (p.Name == ActiveName) return p;
                if (Profiles.Count == 0) Profiles.Add(new Profile());
                ActiveName = Profiles[0].Name;
                return Profiles[0];
            }
        }

        public Profile DefaultProfile
        {
            get
            {
                EnsureDefaultProfile();
                foreach (Profile p in Profiles)
                    if (string.Equals(p.Name, DefaultProfileName,
                        StringComparison.OrdinalIgnoreCase)) return p;
                return Profiles[0];
            }
        }

        /// <summary>
        /// Migração de configurações anteriores: prefere um perfil literalmente
        /// chamado Padrão/Padrao/Default; na ausência dele, conserva o ativo.
        /// </summary>
        public void EnsureDefaultProfile()
        {
            if (Profiles.Count == 0) Profiles.Add(new Profile());
            foreach (Profile p in Profiles)
                if (string.Equals(p.Name, DefaultProfileName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    DefaultProfileName = p.Name;
                    return;
                }

            foreach (Profile p in Profiles)
                if (string.Equals(p.Name, "Padrão", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.Name, "Padrao", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.Name, "Default", StringComparison.OrdinalIgnoreCase))
                {
                    DefaultProfileName = p.Name;
                    return;
                }

            foreach (Profile p in Profiles)
                if (string.Equals(p.Name, ActiveName, StringComparison.OrdinalIgnoreCase))
                {
                    DefaultProfileName = p.Name;
                    return;
                }
            DefaultProfileName = Profiles[0].Name;
        }

        public bool NameExists(string name)
        {
            foreach (Profile p in Profiles)
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static Config Load() { return LoadFrom(FilePath, true); }

        /// <summary>
        /// Le de um caminho explicito.
        ///
        /// Existe para que os testes trabalhem num arquivo temporario. A
        /// tentacao era redirecionar %LOCALAPPDATA% na hora do teste, mas
        /// Environment.GetFolderPath nao le a variavel de ambiente - consulta o
        /// shell. O "isolamento" nao isolava nada e a suite gravou por cima da
        /// configuracao real da maquina. Caminho explicito nao mente.
        /// </summary>
        public static Config LoadFrom(string path, bool migrar)
        {
            Config c = new Config();
            c._savePath = Path.GetFullPath(path);
            try
            {
                if (migrar) MigrateLegacyFile(path);
                if (!File.Exists(path))
                {
                    c.Profiles.Add(new Profile());
                    c.EnsureDefaultProfile();
                    return c;
                }

                Profile current = null;
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith(";")) continue;

                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        string section = line.Substring(1, line.Length - 2).Trim();
                        if (string.Equals(section, "profile", StringComparison.OrdinalIgnoreCase))
                        {
                            current = new Profile();
                            c.Profiles.Add(current);
                        }
                        else current = null;
                        continue;
                    }

                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim();
                    string v = line.Substring(eq + 1).Trim();

                    if (current == null)
                    {
                        if (k == "active") c.ActiveName = v;
                        else if (k == "defaultprofile") c.DefaultProfileName = v;
                        else if (k == "showall") c.ShowAllSensors = (v == "1");
                        else if (k == "language") c.Language = v;
                        else if (k == "idleblank") c.IdleBlankMinutes = ParseInt(v);
                        else if (k == "rotateseconds") c.RotateSeconds = ParseInt(v);
                        else if (k == "sidebarcollapsed") c.SidebarCollapsed = (v == "1");
                        else if (k == "metricschosen") c.MetricsChosen = (v == "1");
                        else if (k == "metricrange") c.MetricRange = MetricHistory.JanelaValida(ParseInt(v));
                        else if (k == "windoww") c.WindowW = ParseInt(v);
                        else if (k == "windowh") c.WindowH = ParseInt(v);
                        else if (k == "gameprofiles") c.GameProfiles = (v == "1");
                        else if (k == "gamemap")
                        {
                            // "jogo.exe|Nome do perfil"
                            int barra = v.IndexOf('|');
                            if (barra <= 0 || barra >= v.Length - 1) continue;
                            c.MapearJogo(v.Substring(0, barra), v.Substring(barra + 1));
                        }
                        else if (k == "gameidentity")
                        {
                            // executavel|nome-base64|caminho-base64. Separado do
                            // gamemap para continuar lendo configuracoes antigas.
                            string[] partes = v.Split('|');
                            if (partes.Length >= 3)
                                c.IdentificarJogo(partes[0], Decode(partes[1]), Decode(partes[2]));
                        }
                        else if (k == "metric")
                        {
                            // "0|hw:..." - tamanho e identificador. Sem a barra e
                            // formato antigo, e vale o tamanho pequeno.
                            if (string.IsNullOrEmpty(v)) continue;
                            int tam = 0; string id = v;
                            int barra = v.IndexOf('|');
                            if (barra > 0)
                            {
                                tam = ParseInt(v.Substring(0, barra));
                                id = v.Substring(barra + 1);
                            }
                            if (id.Length > 0 && !c.MetricIds.Contains(id))
                            {
                                c.MetricIds.Add(id);
                                c.MetricSizes.Add(tam < 0 ? 0 : (tam > 2 ? 2 : tam));
                            }
                        }
                        else if (k == "metricprofile")
                        {
                            MetricProfile profile = ParseMetricProfile(v);
                            if (profile != null && !c.MetricProfileNameExists(profile.Name))
                                c.MetricProfiles.Add(profile);
                        }
                        // chaves legadas (formato antigo, sem seccao)
                        else if (k == "panel1" || k == "panel2" || k == "fahrenheit" || k == "percent")
                        {
                            if (c.Profiles.Count == 0) c.Profiles.Add(new Profile());
                            Apply(c.Profiles[0], k, v);
                        }
                    }
                    else Apply(current, k, v);
                }

                if (c.Profiles.Count == 0) c.Profiles.Add(new Profile());
            }
            catch (Exception ex)
            {
                Log.Error("leitura da configuracao", ex);
                if (c.Profiles.Count == 0) c.Profiles.Add(new Profile());
            }
            c.EnsureDefaultProfile();
            return c;
        }

        private static void Apply(Profile p, string k, string v)
        {
            switch (k)
            {
                case "name": p.Name = v; break;
                case "panel1": p.Panel1Id = v; break;
                case "panel2": p.Panel2Id = v; break;
                case "fahrenheit": p.Fahrenheit = (v == "1"); break;
                case "percent": p.Percent = (v == "1"); break;
                // alert1/alert2 e variantes "low" de versões antigas são
                // deliberadamente ignorados: alertas foram removidos da UI e
                // do ciclo de execução.
                case "divisor1": p.Divisor1 = ParseInt(v); break;
                case "divisor2": p.Divisor2 = ParseInt(v); break;
                case "rotate": p.Rotate = (v == "1"); break;
            }
        }

        private static int ParseInt(string v)
        {
            int n;
            return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : 0;
        }

        private static string Encode(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        }

        private static string Decode(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(value)); }
            catch { return ""; }
        }

        private static MetricProfile ParseMetricProfile(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            string[] parts = value.Split('|');
            if (parts.Length < 2) return null;
            string name = Decode(parts[0]);
            if (string.IsNullOrWhiteSpace(name)) return null;

            MetricProfile profile = new MetricProfile();
            profile.Name = name.Trim();
            profile.Range = MetricHistory.JanelaValida(ParseInt(parts[1]));
            for (int i = 2; i < parts.Length; i++)
            {
                int colon = parts[i].IndexOf(':');
                if (colon <= 0 || colon >= parts[i].Length - 1) continue;
                int size = ParseInt(parts[i].Substring(0, colon));
                string id = Decode(parts[i].Substring(colon + 1));
                if (string.IsNullOrEmpty(id) || profile.Ids.Contains(id)) continue;
                profile.Ids.Add(id);
                profile.Sizes.Add(size < 0 ? 0 : (size > 2 ? 2 : size));
            }
            return profile;
        }

        private static string SerializeMetricProfile(MetricProfile profile)
        {
            StringBuilder line = new StringBuilder();
            line.Append(Encode(profile.Name)).Append('|')
                .Append(MetricHistory.JanelaValida(profile.Range).ToString(CultureInfo.InvariantCulture));
            for (int i = 0; i < profile.Ids.Count; i++)
                if (!string.IsNullOrEmpty(profile.Ids[i]))
                    line.Append('|').Append(profile.Size(i).ToString(CultureInfo.InvariantCulture))
                        .Append(':').Append(Encode(profile.Ids[i]));
            return line.ToString();
        }

        /// <summary>Move um config.ini deixado ao lado do executavel por versoes antigas.</summary>
        private static void MigrateLegacyFile(string target)
        {
            if (File.Exists(target)) return;
            try
            {
                string legacy = Path.Combine(Paths.ExeDir, "config.ini");
                if (!File.Exists(legacy)) return;
                File.Copy(legacy, target);
                File.Delete(legacy);
                Log.Write("config.ini migrado para " + target);
            }
            catch (Exception ex) { Log.Error("migracao do config", ex); }
        }

        /// <summary>Grava a configuracao principal e informa se ela chegou ao disco.</summary>
        public bool Save()
        {
            string erro;
            return Save(out erro);
        }

        public bool Save(out string erro)
        {
            if (string.IsNullOrEmpty(_savePath))
            {
                erro = "configuracao sem destino de persistencia";
                Log.Write("gravacao recusada: " + erro);
                return false;
            }
            return SaveTo(_savePath, out erro);
        }

        /// <summary>Grava num caminho explicito. Veja LoadFrom.</summary>
        public bool SaveTo(string path)
        {
            string erro;
            return SaveTo(path, out erro);
        }

        /// <summary>Grava num caminho explicito e devolve a causa quando falhar.</summary>
        public bool SaveTo(string path, out string erro)
        {
            erro = null;
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("; Mhiagos Control - configuracao");
                sb.AppendLine("[general]");
                sb.AppendLine("active=" + ActiveName);
                EnsureDefaultProfile();
                sb.AppendLine("defaultprofile=" + DefaultProfileName);
                sb.AppendLine("showall=" + (ShowAllSensors ? "1" : "0"));
                if (!string.IsNullOrEmpty(Language)) sb.AppendLine("language=" + Language);
                sb.AppendLine("idleblank=" + IdleBlankMinutes.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("rotateseconds=" + RotateSeconds.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("sidebarcollapsed=" + (SidebarCollapsed ? "1" : "0"));
                sb.AppendLine("metricschosen=" + (MetricsChosen ? "1" : "0"));
                sb.AppendLine("metricrange=" + MetricRange.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("windoww=" + WindowW.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("windowh=" + WindowH.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("gameprofiles=" + (GameProfiles ? "1" : "0"));
                for (int i = 0; i < GameKeys.Count && i < GameProfileNames.Count; i++)
                    if (!string.IsNullOrEmpty(GameKeys[i]) && !string.IsNullOrEmpty(GameProfileNames[i]))
                    {
                        sb.AppendLine("gamemap=" + GameKeys[i] + "|" + GameProfileNames[i]);
                        string nome = i < GameDisplayNames.Count ? GameDisplayNames[i] : "";
                        string caminho = i < GamePaths.Count ? GamePaths[i] : "";
                        if (!string.IsNullOrEmpty(nome) || !string.IsNullOrEmpty(caminho))
                            sb.AppendLine("gameidentity=" + GameKeys[i] + "|" +
                                          Encode(nome) + "|" + Encode(caminho));
                    }
                for (int i = 0; i < MetricIds.Count; i++)
                    if (!string.IsNullOrEmpty(MetricIds[i]))
                        sb.AppendLine("metric=" + MetricSize(i).ToString(CultureInfo.InvariantCulture) +
                                      "|" + MetricIds[i]);
                foreach (MetricProfile profile in MetricProfiles)
                    if (profile != null && !string.IsNullOrWhiteSpace(profile.Name))
                        sb.AppendLine("metricprofile=" + SerializeMetricProfile(profile));
                foreach (Profile p in Profiles) AppendProfile(sb, p);
                WriteAtomically(path, sb.ToString());
                Log.Write("configuracao salva (" + Profiles.Count + " perfis, ativo: " +
                          ActiveName + ", padrao: " + DefaultProfileName + ")");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("gravacao da configuracao", ex);
                erro = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Grava primeiro ao lado do destino e so entao o substitui. Configuracao
        /// parcial parece uma configuracao valida para o leitor e poderia fazer os
        /// perfis parecerem apagados depois de uma queda de energia.
        /// </summary>
        private static void WriteAtomically(string path, string text)
        {
            string temp = path + ".tmp";
            string backup = path + ".bak";
            try
            {
                File.WriteAllText(temp, text, Encoding.UTF8);
                if (File.Exists(path))
                {
                    // File.Replace nao guarda a versao anterior quando o terceiro
                    // argumento e nulo. Uma copia explicita deixa ao menos um
                    // estado recuperavel se uma gravacao valida, mas indevida,
                    // substituir dados do usuario.
                    File.Copy(path, backup, true);
                    File.Replace(temp, path, null);
                }
                else File.Move(temp, path);
            }
            catch
            {
                try { if (File.Exists(temp)) File.Delete(temp); }
                catch { }
                throw;
            }
        }

        private static void AppendProfile(StringBuilder sb, Profile p)
        {
            sb.AppendLine();
            sb.AppendLine("[profile]");
            sb.AppendLine("name=" + p.Name);
            sb.AppendLine("panel1=" + p.Panel1Id);
            sb.AppendLine("panel2=" + p.Panel2Id);
            sb.AppendLine("fahrenheit=" + (p.Fahrenheit ? "1" : "0"));
            sb.AppendLine("percent=" + (p.Percent ? "1" : "0"));
            sb.AppendLine("divisor1=" + p.Divisor1.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("divisor2=" + p.Divisor2.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("rotate=" + (p.Rotate ? "1" : "0"));
        }

        // ---------------- um perfil sozinho ----------------

        /// <summary>
        /// Grava um perfil isolado, no mesmo formato do config.ini.
        ///
        /// Mesmo formato de proposito: o arquivo exportado pode ser lido, e
        /// corrigido, com o bloco de notas, e um config.ini inteiro tambem
        /// serve de origem para importar - o leitor pega o primeiro perfil.
        /// </summary>
        public static bool ExportProfile(Profile p, string path, out string erro)
        {
            erro = null;
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("; Mhiagos Control - perfil exportado");
                AppendProfile(sb, p);
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                Log.Write("perfil exportado: " + p.Name + " -> " + path);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("exportacao de perfil", ex);
                erro = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Le o primeiro perfil de um arquivo. Devolve null quando o arquivo
        /// nao tem perfil nenhum dentro.
        ///
        /// A validacao existe porque LoadFrom nunca falha: diante de lixo ele
        /// devolve um perfil vazio, que sem esta conferencia entraria na lista
        /// do usuario como um "Padrao" mudo.
        /// </summary>
        public static Profile ImportProfile(string path, out string erro)
        {
            erro = null;
            try
            {
                if (!File.Exists(path)) { erro = "arquivo inexistente"; return null; }

                Config c = LoadFrom(path, false);
                if (c.Profiles.Count == 0) { erro = "nenhum perfil no arquivo"; return null; }

                Profile p = c.Profiles[0];
                if (string.IsNullOrEmpty(p.Panel1Id) && string.IsNullOrEmpty(p.Panel2Id))
                {
                    erro = "nenhum perfil no arquivo";
                    return null;
                }
                if (string.IsNullOrEmpty(p.Name)) p.Name = "Importado";
                return p;
            }
            catch (Exception ex)
            {
                Log.Error("importacao de perfil", ex);
                erro = ex.Message;
                return null;
            }
        }

        /// <summary>Nome livre a partir do desejado, acrescentando (2), (3)...</summary>
        public string UniqueName(string desired)
        {
            if (string.IsNullOrEmpty(desired)) desired = "Importado";
            if (!NameExists(desired)) return desired;
            for (int i = 2; i < 1000; i++)
            {
                string n = desired + " (" + i.ToString(CultureInfo.InvariantCulture) + ")";
                if (!NameExists(n)) return n;
            }
            return desired + " " + Guid.NewGuid().ToString("N").Substring(0, 6);
        }
    }
}
