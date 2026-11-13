using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace MhiagosControl
{
    /// <summary>
    /// Alertas no Windows, sem repetir: cada um dispara na passagem para o
    /// estado ruim e so rearma quando o estado volta ao normal.
    /// </summary>
    internal static class Alertas
    {
        private static readonly object _trava = new object();
        private static readonly HashSet<string> _falhasAvisadas = new HashSet<string>();
        private static readonly Dictionary<string, DateTime> _offlineDesde = new Dictionary<string, DateTime>();
        private static readonly HashSet<string> _offlineAvisado = new HashSet<string>();
        private static readonly HashSet<string> _quenteAvisado = new HashSet<string>();
        public static readonly TimeSpan TempoDesconectado = TimeSpan.FromMinutes(5);

        internal static void Zerar()
        {
            lock (_trava) { _falhasAvisadas.Clear(); _offlineDesde.Clear(); _offlineAvisado.Clear(); _quenteAvisado.Clear(); }
        }

        public static void Conferir(Aparelho a, PreferenciasCasa p) { Conferir(a, p, DateTime.Now); }

        internal static void Conferir(Aparelho a, PreferenciasCasa p, DateTime agora)
        {
            List<string[]> avisos = new List<string[]>();
            lock (_trava)
            {
                // falhas do autodiagnostico (f_e_*)
                foreach (KeyValuePair<string, string> kv in a.Estado)
                {
                    if (!kv.Key.StartsWith("f_e_", StringComparison.Ordinal)) continue;
                    string chave = a.Puid + "|" + kv.Key;
                    bool ativa = a.Int(kv.Key, 0) != 0;
                    if (!ativa) { _falhasAvisadas.Remove(chave); continue; }
                    if (_falhasAvisadas.Contains(chave)) continue;
                    _falhasAvisadas.Add(chave);
                    if (!p.AlertaFalha) continue;
                    FalhaOficial f = Diagnostico.Falha(a.Puid, kv.Key);
                    string texto = kv.Key == "f_e_push" ? string.Format(T.CasaAlertaCodigo, a.Int(kv.Key, 0))
                        : T.CasaFalha(kv.Key, null);
                    if (f != null && f.Orientacao.Length > 0) texto += ". " + f.Orientacao;
                    avisos.Add(new[] { T.CasaAlertaFalhaTitulo(a.Nome), texto });
                }

                // desconectado da nuvem por mais de alguns minutos
                if (a.Online) { _offlineDesde.Remove(a.Puid); _offlineAvisado.Remove(a.Puid); }
                else
                {
                    DateTime desde;
                    if (!_offlineDesde.TryGetValue(a.Puid, out desde)) { _offlineDesde[a.Puid] = agora; desde = agora; }
                    if (agora - desde >= TempoDesconectado && !_offlineAvisado.Contains(a.Puid))
                    {
                        _offlineAvisado.Add(a.Puid);
                        if (p.AlertaDesconectado)
                            avisos.Add(new[] { T.CasaAlertaOfflineTitulo(a.Nome), T.CasaAlertaOfflineTexto((int)TempoDesconectado.TotalMinutes) });
                    }
                }

                // ambiente quente com o ar desligado; rearma 1 grau abaixo ou ao ligar
                if (a.Online && a.Tem("f_temp_in"))
                {
                    double amb = a.Num("f_temp_in", 0);
                    bool desligado = a.Int("t_power", 0) == 0;
                    bool fahrenheit = a.Int("t_temp_type", 0) == 1;
                    double limite = fahrenheit ? p.LimiteQuente * 9.0 / 5 + 32 : p.LimiteQuente;
                    if (desligado && amb > limite)
                    {
                        if (!_quenteAvisado.Contains(a.Puid))
                        {
                            _quenteAvisado.Add(a.Puid);
                            if (p.AlertaQuente)
                                avisos.Add(new[] { T.CasaAlertaQuenteTitulo(a.Nome),
                                    T.CasaAlertaQuenteTexto(amb.ToString("0.#") + (fahrenheit ? "°F" : "°C")) });
                        }
                    }
                    else if (!desligado || amb <= limite - 1) _quenteAvisado.Remove(a.Puid);
                }
            }
            foreach (string[] av in avisos) ServicoCasa.AvisoWindows(av[0], av[1]);
        }
    }
}
