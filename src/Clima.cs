using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace MhiagosControl
{
    /// <summary>Uma leitura do tempo lá fora.</summary>
    internal sealed class ClimaAgora
    {
        public double Temperatura;
        public int Codigo = -1;
        public bool Dia = true;
        /// <summary>Quando a leitura chegou (UTC).</summary>
        public DateTime EmUtc;
    }

    /// <summary>Um resultado da busca de cidade.</summary>
    internal sealed class LugarDoClima
    {
        public string Nome, Estado, Pais;
        public double Lat, Lon;

        public string Descricao
        {
            get
            {
                List<string> l = new List<string>();
                if (!string.IsNullOrEmpty(Nome)) l.Add(Nome);
                if (!string.IsNullOrEmpty(Estado)) l.Add(Estado);
                if (!string.IsNullOrEmpty(Pais)) l.Add(Pais);
                return string.Join(", ", l.ToArray());
            }
        }
    }

    /// <summary>
    /// A temperatura lá fora, pela Open-Meteo (gratuita, sem chave, uso não
    /// comercial). A cidade é escolhida em Casa › Ajustes e guardada em
    /// casa.ini com latitude e longitude; nada de localização por IP. Lê a
    /// cada 15 min, ao abrir e ao voltar da suspensão, sempre fora da thread
    /// da interface. A última leitura fica guardada com a hora: sem rede, ela
    /// continua na tela marcada como antiga; sem nada, "--". O log não leva
    /// cidade nem coordenadas.
    /// </summary>
    internal static class Clima
    {
        public const int IntervaloMin = 15;
        /// <summary>Leitura mais velha que isso aparece como antiga.</summary>
        public const int AntigaMin = 45;
        private const string Agente = "MhiagosControl/2.29 (clima; uso pessoal)";

        private static readonly object _trava = new object();
        private static ClimaAgora _atual;
        private static bool _buscando;
        private static Timer _relogio;

        /// <summary>A leitura mudou (ou a cidade). Pode vir de outra thread.</summary>
        public static event EventHandler Mudou;

        /// <summary>Testes: troca o HTTP (recebe a URL, devolve o corpo ou lança).</summary>
        internal static Func<string, string> Baixar = BaixarDeVerdade;

        /// <summary>Testes: o relógio de "agora" (UTC).</summary>
        internal static Func<DateTime> Agora = delegate { return DateTime.UtcNow; };

        private static PreferenciasCasa Pref { get { return ServicoCasa.Pref; } }

        public static bool TemCidade
        {
            get { PreferenciasCasa p = Pref; return p != null && !string.IsNullOrEmpty(p.ClimaCidade) && !double.IsNaN(p.ClimaLat) && !double.IsNaN(p.ClimaLon); }
        }

        public static string Cidade { get { return TemCidade ? Pref.ClimaCidade : ""; } }

        /// <summary>A leitura que há (da memória ou a guardada em casa.ini), ou nula.</summary>
        public static ClimaAgora Atual
        {
            get
            {
                lock (_trava)
                {
                    if (_atual != null) return _atual;
                    PreferenciasCasa p = Pref;
                    if (p != null && !double.IsNaN(p.ClimaTemp) && p.ClimaEmUtc > 0 && TemCidade)
                    {
                        _atual = new ClimaAgora();
                        _atual.Temperatura = p.ClimaTemp; _atual.Codigo = p.ClimaCodigo; _atual.Dia = p.ClimaDia;
                        _atual.EmUtc = new DateTime(p.ClimaEmUtc, DateTimeKind.Utc);
                    }
                    return _atual;
                }
            }
        }

        public static bool Antiga
        {
            get { ClimaAgora a = Atual; return a != null && (Agora() - a.EmUtc).TotalMinutes > AntigaMin; }
        }

        /// <summary>"28,4°" (pt) ou "28.4°" (en); sem leitura, "--".</summary>
        public static string Curto()
        {
            ClimaAgora a = TemCidade ? Atual : null;
            return a == null ? "--" : a.Temperatura.ToString("0.0", Cultura) + "°";
        }

        /// <summary>"28,4 °C"; sem leitura, "--".</summary>
        public static string Texto()
        {
            ClimaAgora a = TemCidade ? Atual : null;
            return a == null ? "--" : a.Temperatura.ToString("0.0", Cultura) + " °C";
        }

        /// <summary>"Recife · 13:15" (hora local da leitura), "Recife · antiga 13:15", ou o convite para definir a cidade.</summary>
        public static string Origem()
        {
            if (!TemCidade) return T.ClimaDefinaCidade;
            ClimaAgora a = Atual;
            if (a == null) return Cidade + " · " + T.ClimaSemLeitura;
            string h = a.EmUtc.ToLocalTime().ToString("HH:mm");
            return Cidade + " · " + (Antiga ? T.ClimaAntiga(h) : h);
        }

        private static CultureInfo Cultura { get { return new CultureInfo(T.Pt ? "pt-BR" : "en-US"); } }

        // ---------------- ciclo ----------------

        /// <summary>Começa a ler (bandeja). Lê já, a cada 15 min e ao voltar da suspensão.</summary>
        public static void Iniciar()
        {
            lock (_trava)
            {
                if (_relogio != null) return;
                _relogio = new Timer(delegate { Atualizar(); }, null, TimeSpan.FromSeconds(3), TimeSpan.FromMinutes(IntervaloMin));
            }
            try
            {
                Microsoft.Win32.SystemEvents.PowerModeChanged += delegate(object s, Microsoft.Win32.PowerModeChangedEventArgs e)
                {
                    if (e.Mode == Microsoft.Win32.PowerModes.Resume) ThreadPool.QueueUserWorkItem(delegate { Thread.Sleep(5000); Atualizar(); });
                };
            }
            catch (Exception ex) { Log.Error("clima: aviso de suspensao", ex); }
        }

        /// <summary>Lê agora, na thread que chamou (o relógio e a suspensão chamam de fora da interface).</summary>
        public static void Atualizar()
        {
            if (!TemCidade) return;
            lock (_trava) { if (_buscando) return; _buscando = true; }
            try
            {
                PreferenciasCasa p = Pref;
                string url = string.Format(CultureInfo.InvariantCulture,
                    "https://api.open-meteo.com/v1/forecast?latitude={0:0.####}&longitude={1:0.####}&current=temperature_2m,weather_code,is_day&timezone=auto",
                    p.ClimaLat, p.ClimaLon);
                ClimaAgora a = InterpretarAtual(Baixar(url));
                if (a == null) { Log.Write("clima: resposta sem temperatura"); return; }
                a.EmUtc = Agora();
                Definir(a);
            }
            catch (Exception ex)
            {
                // sem rede: a última leitura continua (vira "antiga" com o tempo)
                Log.Write("clima: leitura falhou (" + ex.GetType().Name + ")");
            }
            finally { lock (_trava) _buscando = false; }
        }

        /// <summary>Guarda a leitura (memória e casa.ini) e avisa.</summary>
        internal static void Definir(ClimaAgora a)
        {
            lock (_trava) _atual = a;
            PreferenciasCasa p = Pref;
            if (p != null && a != null)
            {
                p.ClimaTemp = a.Temperatura; p.ClimaCodigo = a.Codigo; p.ClimaDia = a.Dia; p.ClimaEmUtc = a.EmUtc.Ticks;
                p.Salvar();
            }
            Avisar();
        }

        /// <summary>Troca a cidade (vinda da busca), apaga a leitura velha e lê de novo, fora da interface.</summary>
        public static void DefinirCidade(LugarDoClima l)
        {
            PreferenciasCasa p = Pref;
            if (p == null) return;
            p.ClimaCidade = l != null ? l.Nome : "";
            p.ClimaLat = l != null ? l.Lat : double.NaN;
            p.ClimaLon = l != null ? l.Lon : double.NaN;
            p.ClimaTemp = double.NaN; p.ClimaEmUtc = 0;
            p.Salvar();
            lock (_trava) _atual = null;
            Avisar();
            if (l != null) ThreadPool.QueueUserWorkItem(delegate { Atualizar(); });
        }

        /// <summary>Testes: esquece a leitura da memória.</summary>
        internal static void Limpar() { lock (_trava) _atual = null; }

        private static void Avisar()
        {
            EventHandler h = Mudou;
            if (h == null) return;
            foreach (EventHandler um in h.GetInvocationList())
            {
                try { um(null, EventArgs.Empty); }
                catch (Exception ex) { Log.Error("clima: aviso", ex); }
            }
        }

        // ---------------- busca de cidade ----------------

        /// <summary>Busca cidades pelo nome (geocodificação da Open-Meteo). Bloqueia: chame fora da interface.</summary>
        public static List<LugarDoClima> BuscarCidade(string nome)
        {
            if (string.IsNullOrEmpty(nome) || nome.Trim().Length < 2) return new List<LugarDoClima>();
            string url = "https://geocoding-api.open-meteo.com/v1/search?name=" + Uri.EscapeDataString(nome.Trim()) +
                "&count=6&language=" + (T.Pt ? "pt" : "en") + "&format=json";
            return InterpretarBusca(Baixar(url));
        }

        // ---------------- JSON ----------------

        /// <summary>{"current":{"temperature_2m":28.4,"weather_code":2,"is_day":1,...}} -> leitura, ou nula.</summary>
        internal static ClimaAgora InterpretarAtual(string json)
        {
            Dictionary<string, object> raiz = Json.Ler(json) as Dictionary<string, object>;
            if (raiz == null) return null;
            object o;
            if (!raiz.TryGetValue("current", out o)) return null;
            Dictionary<string, object> cur = o as Dictionary<string, object>;
            double t;
            if (!Numero(cur, "temperature_2m", out t)) return null;
            ClimaAgora a = new ClimaAgora();
            a.Temperatura = t;
            double c, d;
            if (Numero(cur, "weather_code", out c)) a.Codigo = (int)c;
            if (Numero(cur, "is_day", out d)) a.Dia = d != 0;
            return a;
        }

        /// <summary>{"results":[{"name":..,"admin1":..,"country":..,"latitude":..,"longitude":..}]} -> lugares (vazio sem resultado).</summary>
        internal static List<LugarDoClima> InterpretarBusca(string json)
        {
            List<LugarDoClima> r = new List<LugarDoClima>();
            Dictionary<string, object> raiz = Json.Ler(json) as Dictionary<string, object>;
            object o;
            if (raiz == null || !raiz.TryGetValue("results", out o)) return r;
            List<object> lista = o as List<object>;
            if (lista == null) return r;
            foreach (object x in lista)
            {
                Dictionary<string, object> d = x as Dictionary<string, object>;
                double lat, lon;
                if (d == null || !Numero(d, "latitude", out lat) || !Numero(d, "longitude", out lon)) continue;
                LugarDoClima l = new LugarDoClima();
                l.Nome = Json.Texto(d, "name") ?? "";
                l.Estado = Json.Texto(d, "admin1") ?? "";
                l.Pais = Json.Texto(d, "country") ?? "";
                l.Lat = lat; l.Lon = lon;
                if (l.Nome.Length > 0) r.Add(l);
            }
            return r;
        }

        private static bool Numero(Dictionary<string, object> d, string chave, out double v)
        {
            v = double.NaN;
            object o;
            if (d == null || !d.TryGetValue(chave, out o) || o == null) return false;
            if (o is double) { v = (double)o; return !double.IsNaN(v); }
            return double.TryParse(Convert.ToString(o, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        // ---------------- HTTP ----------------

        private static string BaixarDeVerdade(string url)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.UserAgent = Agente;
            req.Timeout = 8000;
            req.ReadWriteTimeout = 8000;
            req.Accept = "application/json";
            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return sr.ReadToEnd();
        }
    }

    public static partial class T
    {
        public static string ClimaLaFora { get { return P("Lá fora", "Outside"); } }
        public static string ClimaDefinaCidade { get { return P("defina a cidade em Casa › Ajustes", "set the city in Home › Settings"); } }
        public static string ClimaSemLeitura { get { return P("sem leitura", "no reading"); } }
        public static string ClimaAntiga(string h) { return P("antiga, ", "stale, ") + h; }
        public static string ClimaSecao { get { return P("Clima lá fora", "Outside weather"); } }
        public static string ClimaNota { get { return P("A temperatura de fora, pela Open-Meteo (gratuita, sem conta). Escolha a cidade; a leitura é a cada 15 minutos.", "The outside temperature, from Open-Meteo (free, no account). Pick the city; it updates every 15 minutes."); } }
        public static string ClimaBuscar { get { return P("Buscar", "Search"); } }
        public static string ClimaBuscando { get { return P("Buscando…", "Searching…"); } }
        public static string ClimaNenhuma { get { return P("Nenhuma cidade encontrada.", "No city found."); } }
        public static string ClimaFalhaBusca { get { return P("Não foi possível buscar agora. Confira a internet.", "Could not search right now. Check the internet."); } }
        public static string ClimaAtualCidade(string c) { return P("Cidade: ", "City: ") + c; }
        public static string ClimaSemCidade { get { return P("Nenhuma cidade escolhida.", "No city chosen."); } }
    }
}
