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

        /// <summary>Limiar de alerta; 0 desliga. Comparado ao valor JA convertido.</summary>
        public int Alert1 = 0;
        public int Alert2 = 0;

        /// <summary>
        /// Divisor aplicado antes de enviar (1, 10, 100, 1000). Zero significa
        /// "automatico": usa a sugestao para o tipo do sensor.
        /// </summary>
        public int Divisor1 = 0;
        public int Divisor2 = 0;

        public Profile Clone()
        {
            Profile p = new Profile();
            p.Name = Name; p.Panel1Id = Panel1Id; p.Panel2Id = Panel2Id;
            p.Fahrenheit = Fahrenheit; p.Percent = Percent;
            p.Alert1 = Alert1; p.Alert2 = Alert2;
            p.Divisor1 = Divisor1; p.Divisor2 = Divisor2;
            return p;
        }

        public override string ToString() { return Name; }
    }

    /// <summary>
    /// Configuracao completa, em INI seccionado.
    /// Grava em %LOCALAPPDATA%\MhiagosControl\config.ini
    /// </summary>
    public class Config
    {
        public List<Profile> Profiles = new List<Profile>();
        public string ActiveName = "Padrao";

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
            try
            {
                if (migrar) MigrateLegacyFile(path);
                if (!File.Exists(path)) { c.Profiles.Add(new Profile()); return c; }

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
                        else if (k == "showall") c.ShowAllSensors = (v == "1");
                        else if (k == "language") c.Language = v;
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
                case "alert1": p.Alert1 = ParseInt(v); break;
                case "alert2": p.Alert2 = ParseInt(v); break;
                case "divisor1": p.Divisor1 = ParseInt(v); break;
                case "divisor2": p.Divisor2 = ParseInt(v); break;
            }
        }

        private static int ParseInt(string v)
        {
            int n;
            return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : 0;
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

        public void Save() { SaveTo(FilePath); }

        /// <summary>Grava num caminho explicito. Veja LoadFrom.</summary>
        public void SaveTo(string path)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("; Mhiagos Control - configuracao");
                sb.AppendLine("[general]");
                sb.AppendLine("active=" + ActiveName);
                sb.AppendLine("showall=" + (ShowAllSensors ? "1" : "0"));
                if (!string.IsNullOrEmpty(Language)) sb.AppendLine("language=" + Language);
                foreach (Profile p in Profiles)
                {
                    sb.AppendLine();
                    sb.AppendLine("[profile]");
                    sb.AppendLine("name=" + p.Name);
                    sb.AppendLine("panel1=" + p.Panel1Id);
                    sb.AppendLine("panel2=" + p.Panel2Id);
                    sb.AppendLine("fahrenheit=" + (p.Fahrenheit ? "1" : "0"));
                    sb.AppendLine("percent=" + (p.Percent ? "1" : "0"));
                    sb.AppendLine("alert1=" + p.Alert1.ToString(CultureInfo.InvariantCulture));
                    sb.AppendLine("alert2=" + p.Alert2.ToString(CultureInfo.InvariantCulture));
                    sb.AppendLine("divisor1=" + p.Divisor1.ToString(CultureInfo.InvariantCulture));
                    sb.AppendLine("divisor2=" + p.Divisor2.ToString(CultureInfo.InvariantCulture));
                }
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                Log.Write("configuracao salva (" + Profiles.Count + " perfis, ativo: " + ActiveName + ")");
            }
            catch (Exception ex)
            {
                Log.Error("gravacao da configuracao", ex);
            }
        }
    }
}
