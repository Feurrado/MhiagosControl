using System;
using System.Collections.Generic;
using System.Threading;

namespace MhiagosControl
{
    /// <summary>
    /// A sugestao de conforto do servidor da Casa (GET /api/sugestao) para o cartao da Visao geral
    /// da interface nova. Busca em segundo plano a cada 5 minutos, sem travar a interface, e some
    /// em silencio se o servidor for antigo (sem a rota) ou nao responder.
    /// </summary>
    internal static class SugestaoCasa
    {
        private const int ValidadeSeg = 300;
        private static readonly object _trava = new object();
        private static Dictionary<string, object> _atual;
        private static DateTime _em = DateTime.MinValue;
        private static bool _buscando;

        /// <summary>{titulo, texto, aplicavel} ou nulo. Dispara a busca se a ultima estiver velha.</summary>
        public static Dictionary<string, object> Atual()
        {
            if (!ServidorCasa.Configurado) return null;
            bool buscar;
            lock (_trava)
            {
                buscar = !_buscando && (DateTime.UtcNow - _em).TotalSeconds > ValidadeSeg;
                if (buscar) _buscando = true;
            }
            if (buscar) ThreadPool.QueueUserWorkItem(delegate { Buscar(); });
            lock (_trava) return _atual;
        }

        /// <summary>Manda a sugestao ao ar pelo servidor (ele a refaz na hora). Lanca na falha.</summary>
        public static void Aplicar()
        {
            ServidorCasa.AplicarSugestao();
            lock (_trava) _em = DateTime.MinValue;
        }

        private static void Buscar()
        {
            Dictionary<string, object> r = null;
            try { r = ServidorCasa.Sugestao(); }
            catch (Exception ex) { Log.Write("casa: sugestao indisponivel: " + ex.Message); }
            lock (_trava)
            {
                _buscando = false;
                _em = DateTime.UtcNow;
                _atual = Montar(r);
            }
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
            return x;
        }
    }
}
