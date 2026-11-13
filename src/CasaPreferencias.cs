using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace MhiagosControl
{
    /// <summary>Preferencias da pagina Casa, em casa.ini ao lado da config.</summary>
    internal sealed class PreferenciasCasa
    {
        public bool AlertaFalha = true, AlertaDesconectado = true, AlertaQuente = true, AlertaLavadoraFim = true;
        public int LimiteQuente = 28;
        /// <summary>Potencia por modo, em W. Padrao, nao medido: o modelo nao mede consumo.</summary>
        public int PotenciaFrio = 1100, PotenciaEco = 1100, PotenciaSeco = 600, PotenciaVentilar = 60;
        public double Tarifa = 0.85;
        /// <summary>Aba da pagina Casa: 0 Painel, 1 Ajustes.</summary>
        public int AbaCasa;
        /// <summary>Retorno ao padrao depois de uma cena por tempo, e o padrao (24 °C, Frio, ventilador baixo).</summary>
        public long RetornoEmUtc;
        public string RetornoPuid = "";
        public int RetornoModo = 2, RetornoTemp = 24, RetornoVento = 2;
        /// <summary>Cartao de consumo do ar aberto (comeca fechado).</summary>
        /// <summary>Periodo da aba Consumo: 0 sete dias, 1 trinta dias, 2 este mes.</summary>
        public int PeriodoConsumo = 1;
        /// <summary>Secao de consumo e historico da lavadora aberta (comeca fechada).</summary>
        /// <summary>Clima la fora: cidade escolhida (nome e coordenadas) e a ultima leitura guardada.</summary>
        public string ClimaCidade = "";
        public double ClimaLat = double.NaN, ClimaLon = double.NaN, ClimaTemp = double.NaN;
        public int ClimaCodigo = -1;
        public bool ClimaDia = true;
        public long ClimaEmUtc;
        public long DesligarEmUtc;
        public string DesligarPuid = "";
        /// <summary>Servidor da casa (ServidorCasa): endereco, marcas da sincronizacao e agendamentos entregues.</summary>
        public string Servidor = "";
        /// <summary>Endereco externo (https pela internet): tentado primeiro; o interno (Servidor) e a reserva.</summary>
        public string ServidorExterno = "";
        /// <summary>Com o servidor configurado, ler e comandar por ele; a nuvem direta fica de reserva.</summary>
        public bool ServidorFonte = true;
        public long ServidorArAte, ServidorCiclosAte, ServidorEnviadoAte, ServidorDesligarId, ServidorRetornoId, ServidorPrefVersao;
        /// <summary>Resumo das preferencias compartilhadas na ultima troca com o servidor: diferente, mudaram aqui.</summary>
        public string ServidorPrefHash = "";
        /// <summary>Cenas do ar, na ordem dos botoes. Sem nenhuma gravada, as de fabrica.</summary>
        public List<CenaCasa> Cenas = CenaCasa.Padrao();

        internal static string Arquivo
        {
            get { return _arquivo ?? Path.Combine(Paths.DataDir, "casa.ini"); }
            set { _arquivo = value; }
        }
        private static string _arquivo;

        public int Potencia(int modo, string tipo)
        {
            switch (modo)
            {
                case 0: return PotenciaVentilar;
                case 3: return PotenciaSeco;
                case 5: return PotenciaEco;
                default: return PotenciaFrio;
            }
        }

        public static PreferenciasCasa Carregar()
        {
            PreferenciasCasa p = new PreferenciasCasa();
            try
            {
                if (!File.Exists(Arquivo)) return p;
                bool lidas = false;
                foreach (string linha in File.ReadAllLines(Arquivo, Encoding.UTF8))
                {
                    int eq = linha.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = linha.Substring(0, eq).Trim().ToLowerInvariant(), v = linha.Substring(eq + 1).Trim();
                    int i; long l; double d;
                    bool ehInt = int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out i);
                    switch (k)
                    {
                        case "alertafalha": p.AlertaFalha = v == "1"; break;
                        case "alertadesconectado": p.AlertaDesconectado = v == "1"; break;
                        case "alertaquente": p.AlertaQuente = v == "1"; break;
                        case "alertalavadorafim": p.AlertaLavadoraFim = v == "1"; break;
                        case "limitequente": if (ehInt) p.LimiteQuente = Math.Max(16, Math.Min(45, i)); break;
                        case "potenciafrio": if (ehInt) p.PotenciaFrio = Math.Max(0, i); break;
                        case "potenciaeco": if (ehInt) p.PotenciaEco = Math.Max(0, i); break;
                        case "potenciaseco": if (ehInt) p.PotenciaSeco = Math.Max(0, i); break;
                        case "potenciaventilar": if (ehInt) p.PotenciaVentilar = Math.Max(0, i); break;
                        case "tarifa": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d) && d >= 0) p.Tarifa = d; break;
                        case "desligarem": if (long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out l)) p.DesligarEmUtc = l; break;
                        case "desligarpuid": p.DesligarPuid = v; break;
                        // "abacasa" e de quando eram duas abas (1 = Ajustes); "abacasa3": Painel, Consumo, Ajustes.
                        case "abacasa": if (ehInt) p.AbaCasa = i == 1 ? 2 : 0; break;
                        case "abacasa3": if (ehInt) p.AbaCasa = Math.Max(0, Math.Min(2, i)); break;
                        case "periodoconsumo": if (ehInt) p.PeriodoConsumo = Math.Max(0, Math.Min(2, i)); break;
                        case "retornoem": if (long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out l)) p.RetornoEmUtc = l; break;
                        case "retornopuid": p.RetornoPuid = v; break;
                        case "retornomodo": if (ehInt) p.RetornoModo = i; break;
                        case "retornotemp": if (ehInt) p.RetornoTemp = Math.Max(16, Math.Min(32, i)); break;
                        case "retornovento": if (ehInt) p.RetornoVento = i; break;
                        case "climacidade": p.ClimaCidade = v; break;
                        case "climalat": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) p.ClimaLat = d; break;
                        case "climalon": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) p.ClimaLon = d; break;
                        case "climatemp": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) p.ClimaTemp = d; break;
                        case "climacodigo": if (ehInt) p.ClimaCodigo = i; break;
                        case "climadia": p.ClimaDia = v != "0"; break;
                        case "climaem": if (long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out l)) p.ClimaEmUtc = l; break;
                        case "servidor": p.Servidor = v; break;
                        case "servidorexterno": p.ServidorExterno = v; break;
                        case "servidorfonte": p.ServidorFonte = v != "0"; break;
                        case "servidorarate": if (long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out l)) p.ServidorArAte = l; break;
                        case "servidorciclosate": if (long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out l)) p.ServidorCiclosAte = l; break;
                        case "servidorenviadoate": if (long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out l)) p.ServidorEnviadoAte = l; break;
                        case "servidordesligarid": if (long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out l)) p.ServidorDesligarId = l; break;
                        case "servidorretornoid": if (long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out l)) p.ServidorRetornoId = l; break;
                        case "servidorprefversao": if (long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out l)) p.ServidorPrefVersao = l; break;
                        case "servidorprefhash": p.ServidorPrefHash = v; break;
                        case "cena": { CenaCasa cena = CenaCasa.DeLinha(v); if (cena != null) { if (!lidas) { p.Cenas.Clear(); lidas = true; } if (p.Cenas.Count < CenaCasa.MaxCenas) p.Cenas.Add(cena); } break; }
                    }
                }
            }
            catch (Exception ex) { Log.Error("casa: ler casa.ini", ex); }
            return p;
        }

        /// <summary>
        /// Uma gravacao por vez. A interface, a sincronizacao com o servidor e os
        /// timers gravam de threads diferentes; sem a fila, uma gravacao com o
        /// retrato mais velho podia terminar por ultimo e desfazer a outra.
        /// </summary>
        private static readonly object _gravacao = new object();

        public void Salvar()
        {
            lock (_gravacao) SalvarAgora();
        }

        private void SalvarAgora()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("alertafalha=" + (AlertaFalha ? "1" : "0"));
            sb.AppendLine("alertadesconectado=" + (AlertaDesconectado ? "1" : "0"));
            sb.AppendLine("alertaquente=" + (AlertaQuente ? "1" : "0"));
            sb.AppendLine("alertalavadorafim=" + (AlertaLavadoraFim ? "1" : "0"));
            sb.AppendLine("limitequente=" + LimiteQuente.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("potenciafrio=" + PotenciaFrio.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("potenciaeco=" + PotenciaEco.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("potenciaseco=" + PotenciaSeco.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("potenciaventilar=" + PotenciaVentilar.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("tarifa=" + Tarifa.ToString("0.####", CultureInfo.InvariantCulture));
            sb.AppendLine("desligarem=" + DesligarEmUtc.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("desligarpuid=" + (DesligarPuid ?? ""));
            sb.AppendLine("abacasa3=" + AbaCasa.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("periodoconsumo=" + PeriodoConsumo.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("retornoem=" + RetornoEmUtc.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("retornopuid=" + (RetornoPuid ?? ""));
            sb.AppendLine("retornomodo=" + RetornoModo.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("retornotemp=" + RetornoTemp.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("retornovento=" + RetornoVento.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("climacidade=" + (ClimaCidade ?? ""));
            sb.AppendLine("climalat=" + (double.IsNaN(ClimaLat) ? "" : ClimaLat.ToString("R", CultureInfo.InvariantCulture)));
            sb.AppendLine("climalon=" + (double.IsNaN(ClimaLon) ? "" : ClimaLon.ToString("R", CultureInfo.InvariantCulture)));
            sb.AppendLine("climatemp=" + (double.IsNaN(ClimaTemp) ? "" : ClimaTemp.ToString("R", CultureInfo.InvariantCulture)));
            sb.AppendLine("climacodigo=" + ClimaCodigo.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("climadia=" + (ClimaDia ? "1" : "0"));
            sb.AppendLine("climaem=" + ClimaEmUtc.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("servidor=" + (Servidor ?? ""));
            sb.AppendLine("servidorexterno=" + (ServidorExterno ?? ""));
            sb.AppendLine("servidorfonte=" + (ServidorFonte ? "1" : "0"));
            sb.AppendLine("servidorarate=" + ServidorArAte.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("servidorciclosate=" + ServidorCiclosAte.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("servidorenviadoate=" + ServidorEnviadoAte.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("servidordesligarid=" + ServidorDesligarId.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("servidorretornoid=" + ServidorRetornoId.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("servidorprefversao=" + ServidorPrefVersao.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("servidorprefhash=" + (ServidorPrefHash ?? ""));
            // A lista e trocada inteira (a edicao copia antes de mudar): percorrer
            // a referencia lida aqui nao cruza com uma mudanca no meio.
            List<CenaCasa> cenas = Cenas;
            foreach (CenaCasa cena in cenas) sb.AppendLine("cena=" + cena.Linha());
            try { GravacaoAtomica.Escrever(Arquivo, sb.ToString(), new UTF8Encoding(false)); }
            catch (Exception ex) { Log.Error("casa: gravar casa.ini", ex); }
        }
    }
}
