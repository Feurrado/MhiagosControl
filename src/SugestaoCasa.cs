using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;

namespace MhiagosControl
{
    /// <summary>
    /// A sugestao de conforto (GET /api/sugestao) e o tempo da cidade da casa com a qualidade do ar
    /// (GET /api/clima) para o cartao do tempo da Visao geral da interface nova. Busca em segundo
    /// plano, sem travar a interface, e some em silencio se o servidor for antigo (sem a rota) ou nao
    /// responder. O tempo vale 5 minutos. A sugestao vale 1 minuto e e refeita na hora quando o ar muda
    /// (ligou, trocou de modo ou de alvo, o quarto esfriou), para o app nao mostrar outra coisa que o painel web.
    /// </summary>
    internal static class SugestaoCasa
    {
        private const int ValidadeSeg = 300, ValidadeDaSugestaoSeg = 60, MinimoEntreSeg = 5;
        private static readonly object _trava = new object();
        private static Dictionary<string, object> _atual, _tempo;
        private static DateTime _em = DateTime.MinValue, _emSug = DateTime.MinValue, _pedidaEm = DateTime.MinValue;
        private static bool _buscando, _buscandoSug;
        private static string _arVisto;

        /// <summary>O tempo mudou (o widget da area de trabalho redesenha). Vem de outra thread.</summary>
        public static event EventHandler Mudou;

        /// <summary>
        /// {titulo, texto, aplicavel, aplicada} ou nulo. Dispara a busca se a ultima estiver velha ou se o ar mudou
        /// desde ela: estadoDoAr e um resumo do que pesa na sugestao (EstadoDoAr), ou nulo para so o prazo.
        /// </summary>
        public static Dictionary<string, object> Atual(string estadoDoAr)
        {
            if (!ServidorCasa.Configurado) return null;
            bool buscar;
            lock (_trava)
            {
                DateTime agora = DateTime.UtcNow;
                bool arMudou = estadoDoAr != null && estadoDoAr != _arVisto;
                buscar = !_buscandoSug && (agora - _pedidaEm).TotalSeconds >= MinimoEntreSeg && (arMudou || agora > _emSug);
                if (buscar)
                {
                    _buscandoSug = true;
                    _pedidaEm = agora;
                    if (estadoDoAr != null) _arVisto = estadoDoAr;
                }
            }
            if (buscar) ThreadPool.QueueUserWorkItem(delegate { BuscarSugestao(); });
            lock (_trava) return _atual;
        }

        /// <summary>O que pesa na sugestao e muda rapido: ligado, modo, alvo, temperatura e umidade do quarto.</summary>
        internal static string EstadoDoAr(Aparelho ar)
        {
            if (ar == null) return null;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (string k in new string[] { "t_power", "t_work_mode", "t_temp", "f_temp_in", "f_humidity" })
            {
                string v;
                sb.Append(ar.Estado != null && ar.Estado.TryGetValue(k, out v) ? v : "").Append('|');
            }
            return sb.Append(ar.Online ? '1' : '0').ToString();
        }

        /// <summary>{local, temp, texto, icone, max, min, horas, aqi, aqiTexto, aqiCor} ou nulo.</summary>
        public static Dictionary<string, object> Tempo()
        {
            if (!ServidorCasa.Configurado) return null;
            Disparar();
            lock (_trava) return _tempo;
        }

        private static void Disparar()
        {
            bool buscar;
            lock (_trava)
            {
                buscar = !_buscando && (DateTime.UtcNow - _em).TotalSeconds > ValidadeSeg;
                if (buscar) _buscando = true;
            }
            if (buscar) ThreadPool.QueueUserWorkItem(delegate { Buscar(); });
        }

        /// <summary>Manda a sugestao ao ar pelo servidor (ele a refaz na hora). Lanca na falha.</summary>
        public static void Aplicar()
        {
            ServidorCasa.AplicarSugestao();
            // Le a sugestao ja com "aplicada" (o servidor vale o registro nos primeiros segundos, ate a nuvem devolver o estado novo).
            BuscarSugestao();
            lock (_trava) { _pedidaEm = DateTime.MinValue; }
        }

        private static void BuscarSugestao()
        {
            Dictionary<string, object> r = null;
            try { r = ServidorCasa.Sugestao(); }
            catch (Exception ex) { Log.Write("casa: sugestao indisponivel: " + ex.Message); }
            lock (_trava)
            {
                _buscandoSug = false;
                _atual = Montar(r);
                // sem resposta (servidor antigo ou fora do ar) espera o prazo longo, para nao insistir a cada minuto
                _emSug = DateTime.UtcNow.AddSeconds(r == null ? ValidadeSeg : ValidadeDaSugestaoSeg);
            }
        }

        private static void Buscar()
        {
            Dictionary<string, object> c = null;
            try { c = ServidorCasa.Clima(); }
            catch (Exception ex) { Log.Write("casa: clima indisponivel: " + ex.Message); }
            Dictionary<string, object> tempo = MontarTempo(c);
            bool mudou;
            lock (_trava)
            {
                _buscando = false;
                _em = DateTime.UtcNow;
                mudou = Json.Escrever(tempo) != Json.Escrever(_tempo);
                _tempo = tempo;
            }
            EventHandler h = Mudou;
            if (mudou && h != null) h(null, EventArgs.Empty);
        }

        internal static Dictionary<string, object> Montar(Dictionary<string, object> r)
        {
            if (r == null) return null;
            object o;
            Dictionary<string, object> s = r.TryGetValue("sugestao", out o) ? o as Dictionary<string, object> : null;
            if (s == null) return null;
            string titulo = Json.Texto(s, "titulo");
            if (string.IsNullOrEmpty(titulo)) return null;
            Dictionary<string, object> x = new Dictionary<string, object>();
            x["titulo"] = titulo;
            x["texto"] = Json.Texto(s, "texto") ?? "";
            object cena;
            x["aplicavel"] = s.TryGetValue("cena", out cena) && cena != null;
            object ap;
            x["aplicada"] = s.TryGetValue("aplicada", out ap) && ap is bool && (bool)ap;
            return x;
        }

        /// <summary>O essencial do /api/clima para o cartao: agora, maxima e minima de hoje, as 5 proximas horas, 5 dias e o AQI.</summary>
        internal static Dictionary<string, object> MontarTempo(Dictionary<string, object> r)
        {
            Dictionary<string, object> c = Obj(r, "clima"), loc = Obj(r, "local"), ag = Obj(c, "agora");
            double temp;
            if (loc == null || !Num(ag, "temp", out temp)) return null;
            Dictionary<string, object> x = new Dictionary<string, object>();
            string nome = Json.Texto(loc, "nome") ?? "";
            x["local"] = nome.Split(',')[0].Trim();
            x["temp"] = (long)Math.Round(temp);
            x["texto"] = Json.Texto(ag, "texto") ?? "";
            x["icone"] = Json.Texto(ag, "icone") ?? "nuvem";
            object o;
            List<object> dias = c.TryGetValue("dias", out o) ? o as List<object> : null;
            Dictionary<string, object> hoje = dias != null && dias.Count > 0 ? dias[0] as Dictionary<string, object> : null;
            double v;
            x["max"] = Num(hoje, "max", out v) ? (object)(long)Math.Round(v) : null;
            x["min"] = Num(hoje, "min", out v) ? (object)(long)Math.Round(v) : null;
            List<object> horas = new List<object>();
            List<object> hs = c.TryGetValue("horas", out o) ? o as List<object> : null;
            if (hs != null)
                foreach (object h in hs)
                {
                    Dictionary<string, object> d = h as Dictionary<string, object>;
                    string hora = Json.Texto(d, "hora");
                    if (d == null || d.ContainsKey("tipo") || hora == null || hora.Length < 2 || !Num(d, "temp", out v)) continue;
                    Dictionary<string, object> y = new Dictionary<string, object>();
                    y["hora"] = hora.Substring(0, 2) + "h";
                    y["icone"] = Json.Texto(d, "icone") ?? "nuvem";
                    y["temp"] = (long)Math.Round(v);
                    horas.Add(y);
                    if (horas.Count == 5) break;
                }
            x["horas"] = horas;
            List<object> prox = new List<object>();
            if (dias != null)
                foreach (object o2 in dias)
                {
                    Dictionary<string, object> d = o2 as Dictionary<string, object>;
                    string dia = Json.Texto(d, "dia");
                    double mx, mn;
                    if (dia == null || !Num(d, "max", out mx) || !Num(d, "min", out mn)) continue;
                    Dictionary<string, object> y = new Dictionary<string, object>();
                    y["dia"] = dia;
                    y["icone"] = Json.Texto(d, "icone") ?? "nuvem";
                    y["max"] = (long)Math.Round(mx);
                    y["min"] = (long)Math.Round(mn);
                    y["prob"] = Num(d, "prob", out v) ? (object)(long)Math.Round(v) : null;
                    prox.Add(y);
                    if (prox.Count == 5) break;
                }
            x["dias"] = prox;
            Dictionary<string, object> ar = Obj(r, "ar");
            string cor = Json.Texto(ar, "cor");
            x["aqi"] = Num(ar, "aqi", out v) ? (object)(long)Math.Round(v) : null;
            x["aqiTexto"] = Json.Texto(ar, "categoria") ?? "";
            x["aqiCor"] = cor != null && Regex.IsMatch(cor, "^#[0-9A-Fa-f]{6}$") ? cor : "#888888";
            return x;
        }

        private static Dictionary<string, object> Obj(Dictionary<string, object> d, string chave)
        {
            object o;
            return d != null && d.TryGetValue(chave, out o) ? o as Dictionary<string, object> : null;
        }

        private static bool Num(Dictionary<string, object> d, string chave, out double v)
        {
            v = 0;
            object o;
            if (d == null || !d.TryGetValue(chave, out o) || o == null) return false;
            if (o is double) { v = (double)o; return true; }
            if (o is long) { v = (long)o; return true; }
            if (o is int) { v = (int)o; return true; }
            return double.TryParse(o.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }
    }
}
