using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace MhiagosControl
{
    /// <summary>Uma leitura guardada do ar.</summary>
    internal struct AmostraAr
    {
        public DateTime Quando;
        public float Ambiente, Alvo;
        public bool Ligado;
        public int Modo;
    }

    /// <summary>
    /// Historico do ar: uma amostra por leitura real (no maximo uma a cada 50 s),
    /// 35 dias, em casa-historico.txt. Sem leitura nao ha amostra: o grafico
    /// mostra a lacuna, nao inventa linha.
    /// </summary>
    internal static class HistoricoDoAr
    {
        private static readonly object _trava = new object();
        private static readonly Dictionary<string, List<AmostraAr>> _series = new Dictionary<string, List<AmostraAr>>();
        // Dois meses e pouco: o custo do mes compara com o mes anterior inteiro.
        public const int Dias = 65;
        /// <summary>Intervalo maximo entre amostras que ainda conta como tempo continuo.</summary>
        public static readonly TimeSpan Continuo = TimeSpan.FromMinutes(3);

        internal static string Arquivo
        {
            get { return _arquivo ?? Path.Combine(Paths.DataDir, "casa-historico.txt"); }
            set { _arquivo = value; }
        }
        private static string _arquivo;

        internal static void Limpar() { lock (_trava) _series.Clear(); }

        public static void Carregar()
        {
            lock (_trava)
            {
                _series.Clear();
                try
                {
                    if (!File.Exists(Arquivo)) return;
                    DateTime corte = DateTime.Now.AddDays(-Dias);
                    int descartadas = 0;
                    foreach (string linha in File.ReadAllLines(Arquivo, Encoding.UTF8))
                    {
                        string[] c = linha.Split(';');
                        if (c.Length < 6) continue;
                        long s; float amb, alvo; int modo;
                        if (!long.TryParse(c[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out s)) continue;
                        DateTime q = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(s).ToLocalTime();
                        if (q < corte) { descartadas++; continue; }
                        float.TryParse(c[2], NumberStyles.Float, CultureInfo.InvariantCulture, out amb);
                        float.TryParse(c[3], NumberStyles.Float, CultureInfo.InvariantCulture, out alvo);
                        int.TryParse(c[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out modo);
                        AmostraAr a = new AmostraAr();
                        a.Quando = q; a.Ambiente = amb; a.Alvo = alvo; a.Ligado = c[4] == "1"; a.Modo = modo;
                        Serie(c[0]).Add(a);
                    }
                    if (descartadas > 0) Regravar();
                }
                catch (Exception ex) { Log.Error("casa: ler historico", ex); }
            }
        }

        private static List<AmostraAr> Serie(string puid)
        {
            List<AmostraAr> l;
            if (!_series.TryGetValue(puid, out l)) { l = new List<AmostraAr>(); _series[puid] = l; }
            return l;
        }

        /// <summary>Registra a leitura, se o aparelho esta conectado e passou tempo desde a ultima.</summary>
        public static void Registrar(Aparelho a, DateTime agora)
        {
            if (!a.Online || !a.Tem("t_power")) return;
            AmostraAr s = new AmostraAr();
            s.Quando = agora;
            s.Ambiente = a.Tem("f_temp_in") ? (float)a.Num("f_temp_in", 0) : float.NaN;
            s.Alvo = a.Tem("t_temp") ? (float)a.Num("t_temp", 0) : float.NaN;
            s.Ligado = a.Int("t_power", 0) != 0;
            s.Modo = a.Int("t_work_mode", -1);
            lock (_trava)
            {
                List<AmostraAr> l = Serie(a.Puid);
                if (l.Count > 0 && (agora - l[l.Count - 1].Quando).TotalSeconds < 50) return;
                l.Add(s);
                try
                {
                    File.AppendAllText(Arquivo, Linha(a.Puid, s), new UTF8Encoding(false));
                }
                catch (Exception ex) { Log.Error("casa: gravar historico", ex); }
            }
        }

        internal static void Acrescentar(string puid, AmostraAr s) { lock (_trava) Serie(puid).Add(s); }

        /// <summary>
        /// Junta amostras de fora (o servidor da casa): entra so a que nao tem
        /// outra do mesmo ar a menos de 25 s, que e o PC e o servidor lendo a
        /// mesma hora. Devolve quantas entraram; o arquivo fica em ordem.
        /// </summary>
        public static int Mesclar(List<KeyValuePair<string, AmostraAr>> fora)
        {
            DateTime corte = DateTime.Now.AddDays(-Dias);
            int n = 0;
            // O caso de todo minuto e a amostra nova do servidor entrar no FIM da
            // serie: essa so se acrescenta ao arquivo, como faz o Registrar. Regravar
            // tudo (centenas de KB, dentro da trava) fica para quando alguma entra no
            // meio, que e a lacuna de quando o PC esteve desligado.
            bool noMeio = false;
            StringBuilder novas = new StringBuilder();
            lock (_trava)
            {
                foreach (KeyValuePair<string, AmostraAr> kv in fora)
                {
                    if (kv.Key.Length == 0 || kv.Value.Quando < corte) continue;
                    List<AmostraAr> l = Serie(kv.Key);
                    int i = l.FindLastIndex(x => x.Quando <= kv.Value.Quando);
                    if (i >= 0 && (kv.Value.Quando - l[i].Quando).TotalSeconds < 25) continue;
                    if (i + 1 < l.Count && (l[i + 1].Quando - kv.Value.Quando).TotalSeconds < 25) continue;
                    if (i + 1 < l.Count) noMeio = true;
                    else novas.Append(Linha(kv.Key, kv.Value));
                    l.Insert(i + 1, kv.Value);
                    n++;
                }
                if (noMeio) Regravar();
                else if (n > 0)
                {
                    try { File.AppendAllText(Arquivo, novas.ToString(), new UTF8Encoding(false)); }
                    catch (Exception ex) { Log.Error("casa: gravar historico", ex); }
                }
            }
            return n;
        }

        /// <summary>As amostras de todos os ares depois da data, para mandar ao servidor.</summary>
        public static List<KeyValuePair<string, AmostraAr>> Desde(DateTime desde)
        {
            List<KeyValuePair<string, AmostraAr>> r = new List<KeyValuePair<string, AmostraAr>>();
            lock (_trava)
                foreach (KeyValuePair<string, List<AmostraAr>> kv in _series)
                    foreach (AmostraAr s in kv.Value)
                        if (s.Quando > desde) r.Add(new KeyValuePair<string, AmostraAr>(kv.Key, s));
            return r;
        }

        private static string Linha(string puid, AmostraAr s)
        {
            long seg = (long)(s.Quando.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            return puid + ";" + seg.ToString(CultureInfo.InvariantCulture) + ";" +
                s.Ambiente.ToString("0.##", CultureInfo.InvariantCulture) + ";" +
                s.Alvo.ToString("0.##", CultureInfo.InvariantCulture) + ";" + (s.Ligado ? "1" : "0") + ";" +
                s.Modo.ToString(CultureInfo.InvariantCulture) + "\n";
        }

        private static void Regravar()
        {
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<string, List<AmostraAr>> kv in _series)
                foreach (AmostraAr s in kv.Value) sb.Append(Linha(kv.Key, s));
            try { GravacaoAtomica.Escrever(Arquivo, sb.ToString(), new UTF8Encoding(false)); }
            catch (Exception ex) { Log.Error("casa: regravar historico", ex); }
        }

        public static List<AmostraAr> Amostras(string puid, DateTime desde)
        {
            lock (_trava)
            {
                List<AmostraAr> r = new List<AmostraAr>();
                List<AmostraAr> l;
                if (_series.TryGetValue(puid, out l))
                    foreach (AmostraAr s in l) if (s.Quando >= desde) r.Add(s);
                return r;
            }
        }

        /// <summary>
        /// Tempo ligado e energia ESTIMADA por dia, entre duas datas. Cada amostra
        /// vale ate a seguinte, limitado a 3 min: lacuna maior e tempo sem leitura,
        /// e nao entra na conta.
        /// </summary>
        public static SortedDictionary<DateTime, double[]> PorDia(string puid, DateTime de, DateTime ate, PreferenciasCasa p, string tipo)
        {
            SortedDictionary<DateTime, double[]> d = new SortedDictionary<DateTime, double[]>();
            for (DateTime dia = de.Date; dia <= ate.Date; dia = dia.AddDays(1)) d[dia] = new double[2];   // [horas, kWh]
            List<AmostraAr> l = Amostras(puid, de.Date);
            for (int i = 0; i < l.Count; i++)
            {
                AmostraAr s = l[i];
                if (!s.Ligado || s.Quando > ate) continue;
                DateTime fim = i + 1 < l.Count ? l[i + 1].Quando : s.Quando.Add(TimeSpan.FromMinutes(1));
                if (fim - s.Quando > Continuo) fim = s.Quando + Continuo;
                if (fim > ate) fim = ate;
                // Divide no meio da noite, para o trecho cair no dia certo.
                DateTime ini = s.Quando;
                while (ini < fim)
                {
                    DateTime corte = ini.Date.AddDays(1) < fim ? ini.Date.AddDays(1) : fim;
                    double h = (corte - ini).TotalHours;
                    double[] v;
                    if (d.TryGetValue(ini.Date, out v))
                    {
                        v[0] += h;
                        v[1] += h * p.Potencia(s.Modo, tipo) / 1000.0;
                    }
                    ini = corte;
                }
            }
            return d;
        }
    }
}
