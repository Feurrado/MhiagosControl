using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// Leitura das propriedades da lava e seca Hisense (tipo 025, como a
    /// WD3S1114 do Thiago). Os nomes dos valores vem da propria
    /// get_property_list do modelo (ex.: machine_status 0:Standby,1:Open,
    /// 2:Running,3:Pause,4:Error); aqui so sao traduzidos. O nome dos
    /// programas nao vem da nuvem: vai pelo numero ate calibrar.
    /// </summary>
    internal static class TabelaDaLavadora
    {
        /// <summary>
        /// Nomes CONFIRMADOS para o modelo do Thiago (025-1wj110009v0t), pelo
        /// painel da maquina.
        /// </summary>
        internal static readonly Dictionary<int, string[]> Confirmados = new Dictionary<int, string[]>
        {
            // Confirmado pelo Thiago em 26/10/2026: o ciclo das 21:20 (programa 7) foi Algodao.
            { 7, new[] { "Algodão", "Cotton" } }
        };

        /// <summary>
        /// Nomes PROVAVEIS, do dicionario do connectlife-ha para as lava e secas
        /// da mesma familia (025-1wj120514v0t e 025-1wj120495v0t concordam nos ids
        /// em comum; 7 = cotton). Nao ha arquivo para 1wj110009v0t, e outra familia
        /// (1wj100649v0t) numera diferente: por isso aparecem como "provavel".
        /// Par pt/en.
        /// </summary>
        internal static readonly Dictionary<int, string[]> Provaveis = new Dictionary<int, string[]>
        {
            { 1, new[] { "Algodão + secagem", "Cotton dry" } },
            { 2, new[] { "Sintético + secagem", "Synthetic dry" } },
            { 4, new[] { "Refrescar", "Refresh" } },
            { 5, new[] { "Antialérgico", "Allergy care" } },
            { 6, new[] { "Limpeza do tambor", "Drum clean" } },
            { 7, new[] { "Algodão", "Cotton" } },
            { 8, new[] { "Sintético", "Synthetic" } },
            { 9, new[] { "Eco 40-60", "Eco 40-60" } },
            { 10, new[] { "Lã", "Wool" } },
            { 11, new[] { "Rápido 15'", "Quick 15'" } },
            { 12, new[] { "Misto", "Mix" } },
            { 14, new[] { "Centrifugar", "Spin" } },
            { 16, new[] { "Bebê", "Baby care" } },
            { 18, new[] { "Roupa esportiva", "Sportswear" } },
            { 19, new[] { "Edredom", "Down" } },
            { 20, new[] { "Enxaguar e centrifugar", "Rinse and spin" } },
            { 21, new[] { "Delicadas", "Delicates" } },
            { 22, new[] { "Lavar e secar 60'", "Clean and dry 60'" } },
            { 24, new[] { "Camisas", "Shirts" } },
            { 41, new[] { "Power 49'", "Power 49'" } },
            { 42, new[] { "Automático", "Auto" } },
            { 44, new[] { "Roupa de cama", "Bedding" } },
            { 45, new[] { "Jeans", "Jeans" } },
            { 55, new[] { "Lavagem à mão", "Hand wash" } }
        };

        public static string Programa(int id)
        {
            if (id <= 0) return T.LavSemPrograma;
            string[] n;
            if (Confirmados.TryGetValue(id, out n)) return T.Pt ? n[0] : n[1];
            if (Provaveis.TryGetValue(id, out n)) return string.Format(T.LavProgramaProvavel, id, T.Pt ? n[0] : n[1]);
            return string.Format(T.LavProgramaN, id);
        }

        public static string Fase(int f)
        {
            switch (f)
            {
                case 1: return T.LavFasePesando;
                case 2: return T.LavFasePrelavagem;
                case 3: return T.LavFaseLavagem;
                case 4: return T.LavFaseEnxague;
                case 5: return T.LavFaseAmaciante;
                case 6: return T.LavFaseAntirrugas;
                case 7: return T.LavFaseCentrifugacao;
                case 8: return T.LavFaseSecagem;
                case 9: return T.LavFaseArejando;
                case 10: return T.LavFaseFim;
                case 11: return T.LavFaseProgramado;
                default: return "";
            }
        }

        public static string Porta(int v)
        {
            switch (v)
            {
                case 1: return T.LavPortaDestravada;
                case 2: return T.LavPortaTravada;
                case 3: return T.LavPortaFechada;   // 3 nao esta na lista do modelo; no connectlife-ha e "closed"
                default: return "--";
            }
        }

        public static string Temperatura(int v) { return v <= 0 ? T.LavFria : (v * 10) + " °C"; }

        public static string Centrifugacao(int v) { return v <= 0 ? T.LavSemCentrifugar : (v * 100) + " rpm"; }

        public static string Secagem(int v)
        {
            switch (v)
            {
                case 0: return T.LavSemSecagem;
                case 1: return T.LavSecaPassar;
                case 2: return T.LavSecaVestir;
                case 3: return T.LavSecaGuardar;
                case 4: return T.LavSecaIntensa;
                default: return v + " min";
            }
        }

        public static string Sujeira(int v)
        {
            switch (v)
            {
                case 1: return T.LavSujeiraLeve;
                case 2: return T.LavSujeiraMedia;
                case 3: return T.LavSujeiraPesada;
                default: return T.LavSujeiraNormal;
            }
        }
    }

    /// <summary>O estado do ciclo, lido das propriedades (com as grafias que a WD3S usa).</summary>
    internal sealed class CicloAtual
    {
        public bool Online, Rodando, Pausada, Erro, Concluido, Programado;
        public int Status = -1, Fase, Programa, Restante, Total, CodigoDeErro;
        public float Progresso;
        public double? Agua, Energia;

        public static CicloAtual De(Aparelho a)
        {
            CicloAtual c = new CicloAtual();
            if (a == null) return c;
            c.Online = a.Online;
            c.Status = a.Int("machine_status", a.Int("DeviceStatus", -1));
            c.Fase = a.Int("Current_program_phase", a.Int("CurrentProgramPhase", 0));
            c.Programa = a.Int("Selected_program_ID", a.Int("Program", 0));
            c.Restante = a.Int("Selected_program_remaining_time_in_minutes", a.Int("Remaining_time_in_minutes", 0));
            c.Total = a.Int("Selected_program_total_running_time_in_minutes", a.Int("Selected_program_total_time_in_minutes", 0));
            c.CodigoDeErro = a.Int("error_code", 0);
            c.Erro = c.Status == 4 || c.CodigoDeErro != 0;
            c.Pausada = c.Status == 3;
            c.Rodando = c.Status == 2 || c.Pausada;
            c.Programado = c.Rodando && c.Fase == 11;
            c.Concluido = c.Fase == 10;
            if (c.Rodando && c.Total > 0) c.Progresso = Math.Max(0f, Math.Min(1f, 1f - (float)c.Restante / c.Total));
            else if (c.Concluido) c.Progresso = 1f;
            if (a.Tem("Water_consumption_int")) c.Agua = a.Int("Water_consumption_int", 0) + a.Int("Water_consumption_decimal", 0) / 100.0;
            if (a.Tem("Electricit_consumption_int")) c.Energia = a.Int("Electricit_consumption_int", 0) + a.Int("Electricit_consumption_decimal", 0) / 100.0;
            return c;
        }

        /// <summary>O titulo do painel: o que a maquina esta fazendo agora.</summary>
        public string Titulo()
        {
            if (!Online) return T.LavDesconectada;
            if (Erro) return CodigoDeErro != 0 ? string.Format(T.LavErroCodigo, CodigoDeErro) : T.LavErro;
            if (Pausada) return T.LavPausada;
            if (Programado) return T.LavFaseProgramado;
            if (Rodando)
            {
                string f = TabelaDaLavadora.Fase(Fase);
                return f.Length > 0 ? f : T.LavEmFuncionamento;
            }
            if (Concluido) return T.LavConcluida;
            return Status == 1 ? T.LavLigada : T.LavEmEspera;
        }
    }

    /// <summary>
    /// Ciclos da lavadora: inicio, fim, programa, agua e energia do ciclo, em
    /// casa-ciclos.txt. O inicio e quando a leitura passa a "em funcionamento"
    /// (fora do aguardo do inicio programado); o fim, quando deixa de estar.
    /// Avisa no Windows ao terminar e ao dar erro.
    /// </summary>
    internal static class CiclosDaLavadora
    {
        internal sealed class Ciclo
        {
            public DateTime Inicio, Fim;
            public int Programa;
            public double Agua, Energia;
            public bool Concluido;
        }

        private static readonly object _trava = new object();
        private static readonly Dictionary<string, Ciclo> _emCurso = new Dictionary<string, Ciclo>();
        private static readonly Dictionary<string, List<Ciclo>> _feitos = new Dictionary<string, List<Ciclo>>();
        private static readonly HashSet<string> _erroAvisado = new HashSet<string>();
        private static bool _carregado;

        internal static string Arquivo
        {
            get { return _arquivo ?? Path.Combine(Paths.DataDir, "casa-ciclos.txt"); }
            set { _arquivo = value; }
        }
        private static string _arquivo;

        internal static void Zerar()
        {
            lock (_trava) { _emCurso.Clear(); _feitos.Clear(); _erroAvisado.Clear(); _carregado = true; }
        }

        private static void Carregar()
        {
            if (_carregado) return;
            _carregado = true;
            try
            {
                if (!File.Exists(Arquivo)) return;
                foreach (string linha in File.ReadAllLines(Arquivo, Encoding.UTF8))
                {
                    string[] c = linha.Split(';');
                    if (c.Length < 7) continue;
                    Ciclo x = new Ciclo();
                    long i, f;
                    if (!long.TryParse(c[1], out i) || !long.TryParse(c[2], out f)) continue;
                    x.Inicio = Epoca(i); x.Fim = Epoca(f);
                    int.TryParse(c[3], out x.Programa);
                    double.TryParse(c[4], NumberStyles.Float, CultureInfo.InvariantCulture, out x.Agua);
                    double.TryParse(c[5], NumberStyles.Float, CultureInfo.InvariantCulture, out x.Energia);
                    x.Concluido = c[6] == "1";
                    Lista(c[0]).Add(x);
                }
            }
            catch (Exception ex) { Log.Error("casa: ler ciclos", ex); }
        }

        private static DateTime Epoca(long s) { return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(s).ToLocalTime(); }
        private static long Seg(DateTime d) { return (long)(d.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds; }

        private static List<Ciclo> Lista(string puid)
        {
            List<Ciclo> l;
            if (!_feitos.TryGetValue(puid, out l)) { l = new List<Ciclo>(); _feitos[puid] = l; }
            return l;
        }

        /// <summary>Os ultimos ciclos, do mais novo ao mais antigo.</summary>
        public static List<Ciclo> Ultimos(string puid, int n)
        {
            lock (_trava)
            {
                Carregar();
                List<Ciclo> l = new List<Ciclo>(Lista(puid));
                l.Reverse();
                if (l.Count > n) l.RemoveRange(n, l.Count - n);
                return l;
            }
        }

        /// <summary>Ciclos, litros e kWh dos ciclos registrados no mes da data dada.</summary>
        public static double[] DoMes(string puid, DateTime dia)
        {
            double[] r = new double[3];
            lock (_trava)
            {
                Carregar();
                foreach (Ciclo x in Lista(puid))
                    if (x.Inicio.Year == dia.Year && x.Inicio.Month == dia.Month) { r[0]++; r[1] += x.Agua; r[2] += x.Energia; }
            }
            return r;
        }
        public static void Conferir(Aparelho a, PreferenciasCasa p) { Conferir(a, p, DateTime.Now); }

        internal static void Conferir(Aparelho a, PreferenciasCasa p, DateTime agora)
        {
            if (!a.Online) return;   // desconectada: nao da para saber se terminou
            CicloAtual c = CicloAtual.De(a);
            List<string[]> avisos = new List<string[]>();
            lock (_trava)
            {
                Carregar();
                Ciclo atual;
                _emCurso.TryGetValue(a.Puid, out atual);
                bool trabalhando = c.Rodando && !c.Programado && !c.Concluido;
                if (trabalhando)
                {
                    if (atual == null)
                    {
                        atual = new Ciclo();
                        atual.Inicio = agora;
                        _emCurso[a.Puid] = atual;
                    }
                    atual.Programa = c.Programa;
                    if (c.Agua.HasValue) atual.Agua = c.Agua.Value;
                    if (c.Energia.HasValue) atual.Energia = c.Energia.Value;
                }
                else if (atual != null)
                {
                    atual.Fim = agora;
                    atual.Concluido = c.Concluido || c.Status == 0 || c.Status == 1;
                    if (c.Agua.HasValue) atual.Agua = c.Agua.Value;
                    if (c.Energia.HasValue) atual.Energia = c.Energia.Value;
                    _emCurso.Remove(a.Puid);
                    Lista(a.Puid).Add(atual);
                    try
                    {
                        File.AppendAllText(Arquivo, a.Puid + ";" + Seg(atual.Inicio) + ";" + Seg(atual.Fim) + ";" + atual.Programa + ";" +
                            atual.Agua.ToString("0.##", CultureInfo.InvariantCulture) + ";" +
                            atual.Energia.ToString("0.##", CultureInfo.InvariantCulture) + ";" + (atual.Concluido ? "1" : "0") + "\n",
                            new UTF8Encoding(false));
                    }
                    catch (Exception ex) { Log.Error("casa: gravar ciclo", ex); }
                    if (p.AlertaLavadoraFim && !c.Erro)
                        avisos.Add(new[] { T.LavAvisoFimTitulo(a.Nome), T.LavAvisoFimTexto(TabelaDaLavadora.Programa(atual.Programa),
                            Duracao(atual.Fim - atual.Inicio)) });
                }

                if (c.Erro)
                {
                    if (!_erroAvisado.Contains(a.Puid))
                    {
                        _erroAvisado.Add(a.Puid);
                        if (p.AlertaFalha)
                            avisos.Add(new[] { T.LavAvisoErroTitulo(a.Nome), c.Titulo() });
                    }
                }
                else _erroAvisado.Remove(a.Puid);
            }
            foreach (string[] av in avisos) ServicoCasa.AvisoWindows(av[0], av[1]);
        }

        public static string Duracao(TimeSpan d)
        {
            int min = (int)Math.Round(d.TotalMinutes);
            return min < 60 ? min + " min" : string.Format("{0} h {1:00}", min / 60, min % 60);
        }
    }

    /// <summary>
    /// Cabecalho de uma secao recolhivel dentro de um cartao: titulo, resumo a
    /// direita do titulo e a seta. O clique e de quem usa (Click).
    /// </summary>
    internal sealed class CabecalhoRecolhivel : Control
    {
        public string Titulo = "", Resumo = "";
        public bool Aberto;
        private Transicao _t;
        private Transicao TH { get { return _t ?? (_t = new Transicao(this)); } }

        public CabecalhoRecolhivel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleRole = AccessibleRole.PushButton;
        }

        protected override void OnMouseEnter(EventArgs e) { TH.Ir(1f); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { TH.Ir(0f); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(Ui.Misturar(Ui.Surface, Ui.Hover, TH.Valor))) g.FillRectangle(b, ClientRectangle);
            using (Pen p = new Pen(Ui.Border)) g.DrawLine(p, 0, 0, Width, 0);
            AccessibleName = Titulo;
            Size t = TextRenderer.MeasureText(Titulo, Ui.FontMed);
            TextRenderer.DrawText(g, Titulo, Ui.FontMed, new Rectangle(0, 0, t.Width + Ui.S(4), Height), Ui.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            Rectangle seta = new Rectangle(Width - Ui.S(22), (Height - Ui.S(22)) / 2, Ui.S(22), Ui.S(22));
            TextRenderer.DrawText(g, Aberto ? "\uE70E" : "\uE70D", Ui.FontGlyph9, seta, Ui.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            int rx = t.Width + Ui.S(12);
            if (Resumo.Length > 0)
                TextRenderer.DrawText(g, Resumo, Ui.FontSmall, new Rectangle(rx, 0, Math.Max(0, seta.Left - rx - Ui.S(8)), Height), Ui.Muted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues) using (Pen p = new Pen(Ui.Accent, 2f)) g.DrawRectangle(p, 1, 1, Width - 3, Height - 3);
        }
    }
    /// <summary>Anel de progresso do ciclo, com o tempo restante no meio.</summary>
    internal sealed class AnelDoCiclo : Control
    {
        public float Progresso;
        public string Grande = "--", Pequeno = "", Rodape = "";
        public Color Cor = Ui.Accent;
        public bool Apagado;
        private static readonly Font FonteGrande = new Font(Ui.Tipo.Exibicao, Ui.Pt(26f));

        public AnelDoCiclo()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(Ui.Surface)) g.FillRectangle(b, ClientRectangle);
            Ui.Smooth(g);
            int esp = Ui.S(12);
            float lado = Math.Min(Width, Height) - esp - Ui.S(4);
            RectangleF r = new RectangleF((Width - lado) / 2f, (Height - lado) / 2f, lado, lado);
            using (Pen p = new Pen(Ui.SurfaceAlt, esp)) g.DrawEllipse(p, r);
            if (Progresso > 0.002f)
                using (Pen p = new Pen(Apagado ? Ui.Faint : Cor, esp))
                {
                    p.StartCap = p.EndCap = LineCap.Round;
                    g.DrawArc(p, r, -90f, 360f * Math.Min(1f, Progresso));
                }
            Rectangle m = Rectangle.Round(r);
            // Anel pequeno (o da Visao geral): fonte menor, para o tempo caber dentro.
            Font fg = Width < Ui.S(140) ? Ui.FontSemi : FonteGrande;
            Size sg = TextRenderer.MeasureText(g, Grande, fg, Size.Empty, TextFormatFlags.NoPadding);
            int y = m.Y + m.Height / 2 - sg.Height / 2 - (Pequeno.Length > 0 || Rodape.Length > 0 ? Ui.S(8) : 0);
            TextRenderer.DrawText(g, Grande, fg, new Rectangle(m.X, y, m.Width, sg.Height), Apagado ? Ui.Muted : Ui.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, Pequeno, Ui.FontSmall, new Rectangle(m.X, y + sg.Height + Ui.S(2), m.Width, Ui.S(18)), Ui.Muted,
                TextFormatFlags.HorizontalCenter);
            if (Rodape.Length > 0)
                TextRenderer.DrawText(g, Rodape, Ui.FontSmall, new Rectangle(m.X, y + sg.Height + Ui.S(20), m.Width, Ui.S(18)),
                    Apagado ? Ui.Muted : Ui.AccentText, TextFormatFlags.HorizontalCenter);
        }
    }

    /// <summary>A linha de etapas do ciclo: feitas, a atual e as que faltam.</summary>
    internal sealed class EtapasDoCiclo : Control
    {
        public readonly List<string> Nomes = new List<string>();
        public int Atual = -1;   // indice; Nomes.Count = tudo feito
        public Color Cor = Ui.Accent;

        public EtapasDoCiclo()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        /// <summary>
        /// Preenche os nomes das etapas (Secar so quando o ciclo seca) e devolve
        /// o indice da atual pela fase, ou -1. Usado aqui e na Visao geral.
        /// </summary>
        internal static int Montar(Aparelho a, CicloAtual c, List<string> nomes)
        {
            bool seca = a.Int("dry_time", 0) > 0 || a.Int("DryingSwitch", 0) == 1 || c.Fase == 8 || c.Fase == 9;
            nomes.Clear();
            nomes.Add(T.LavEtapaLavar);
            nomes.Add(T.LavEtapaEnxaguar);
            nomes.Add(T.LavEtapaCentrifugar);
            if (seca) nomes.Add(T.LavEtapaSecar);
            nomes.Add(T.LavEtapaFim);
            switch (c.Fase)
            {
                case 1: case 2: case 3: return 0;
                case 4: case 5: return 1;
                case 6: case 7: return 2;
                case 8: case 9: return seca ? 3 : 2;
                case 10: return nomes.Count - 1;
                default: return -1;
            }
        }

        private int Diametro { get { return Ui.S(14); } }
        private int Halo { get { return Ui.S(6); } }
        private int Yc { get { return Ui.S(12); } }

        /// <summary>
        /// Centro de cada marcador. As pontas recuam o raio mais o halo do atual,
        /// para o primeiro e o ultimo nao encostarem na borda.
        /// </summary>
        internal float[] Centros()
        {
            int n = Nomes.Count;
            float[] x = new float[n];
            float ini = Diametro / 2f + Halo + 1, fim = Width - Diametro / 2f - Halo - 1;
            for (int i = 0; i < n; i++) x[i] = n == 1 ? Width / 2f : ini + i * (fim - ini) / (n - 1);
            return x;
        }

        /// <summary>
        /// Onde cada rotulo cabe: o primeiro alinhado a esquerda, o ultimo a
        /// direita, os do meio centrados no marcador com metade do espaco ate os vizinhos.
        /// </summary>
        internal Rectangle[] Rotulos()
        {
            int n = Nomes.Count;
            float[] x = Centros();
            Rectangle[] r = new Rectangle[n];
            int y = Yc + Halo + Ui.S(6), h = Ui.S(20);
            for (int i = 0; i < n; i++)
            {
                float esq = i == 0 ? 0 : (x[i - 1] + x[i]) / 2f;
                float dir = i == n - 1 ? Width : (x[i] + x[i + 1]) / 2f;
                r[i] = Rectangle.FromLTRB((int)Math.Ceiling(esq), y, (int)Math.Floor(dir), y + h);
            }
            return r;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(Ui.Surface)) g.FillRectangle(b, ClientRectangle);
            Ui.Smooth(g);
            int n = Nomes.Count;
            if (n == 0) return;
            int d = Diametro, yc = Yc;
            float[] x = Centros();
            Rectangle[] rot = Rotulos();
            for (int i = 0; i < n - 1; i++)
                using (Pen p = new Pen(i < Atual ? Cor : Ui.Border, Ui.S(2))) g.DrawLine(p, x[i], yc, x[i + 1], yc);
            for (int i = 0; i < n; i++)
            {
                RectangleF bola = new RectangleF(x[i] - d / 2f, yc - d / 2f, d, d);
                bool feita = i < Atual, atual = i == Atual;
                using (SolidBrush b = new SolidBrush(feita || atual ? Cor : Ui.Surface)) g.FillEllipse(b, bola);
                using (Pen p = new Pen(feita || atual ? Cor : Ui.Border, Ui.S(2))) g.DrawEllipse(p, bola);
                if (atual)
                    using (Pen p = new Pen(Color.FromArgb(90, Cor), Ui.S(3)))
                        g.DrawEllipse(p, RectangleF.Inflate(bola, Ui.S(4), Ui.S(4)));
                TextFormatFlags al = i == 0 ? TextFormatFlags.Left : i == n - 1 ? TextFormatFlags.Right : TextFormatFlags.HorizontalCenter;
                TextRenderer.DrawText(g, Nomes[i], atual ? Ui.FontMed : Ui.FontSmall, rot[i],
                    atual ? Ui.Text : feita ? Ui.Muted : Ui.Faint, al | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            }
        }
    }
    /// <summary>
    /// Painel da lava e seca: anel com o tempo restante e a hora de termino,
    /// o que a maquina esta fazendo, as etapas do ciclo, os parametros, porta,
    /// consumo do ciclo e os ultimos ciclos. So leitura.
    /// </summary>
    internal sealed class CartaoDaLavadora : CartaoDeAparelho
    {
        private readonly AnelDoCiclo _anel;
        private readonly EtapasDoCiclo _etapas;
        private readonly Label _titulo, _programa, _termino, _aviso, _lbParams, _lbCiclos, _ciclos, _tudo;
        private readonly List<Label[]> _params = new List<Label[]>();
        private readonly FlatBtn _btTudo;
        private bool _aberto;
        private readonly BotaoChave _chave;
        private readonly ToolTip _dica = new ToolTip();
        private readonly Label _diag, _lbDiag;
        private bool _diagAberto;
        internal BotaoChave Chave { get { return _chave; } }

        /// <summary>Mostra ou esconde o diagnostico (so leitura: a lavadora esta em "so monitorar").</summary>
        internal void AlternarDiagnostico()
        {
            _diagAberto = !_diagAberto;
            _chave.Aberto = _diagAberto;
            _chave.Invalidate();
            _diag.Visible = _lbDiag.Visible = _diagAberto;
            if (_diagAberto && A != null) _diag.Text = Diagnostico.Texto(A);
            AvisarAltura();
        }

        private readonly CabecalhoRecolhivel _cabConsumo;
        private readonly Label _consumo;
        private readonly AnimacaoDeRecolher _animConsumo;

        internal CabecalhoRecolhivel CabecalhoDoConsumo { get { return _cabConsumo; } }
        private bool _compacto;
        /// <summary>Sem ciclo rodando (ou desconectada): so nome e estado.</summary>
        internal bool Compacto { get { return _compacto; } }
        public bool ConsumoAberto { get { return _animConsumo.Aberto; } }

        /// <summary>Abre ou fecha a secao de consumo e historico, lembrando em casa.ini.</summary>
        public void AlternarConsumo(bool animar)
        {
            bool abrir = !_animConsumo.Aberto;
            ServicoCasa.Pref.ConsumoLavadoraAberto = abrir;
            ServicoCasa.Pref.Salvar();
            _animConsumo.Ir(abrir, animar && IsHandleCreated && Visible);
        }

        private void AjustarConsumo()
        {
            bool algo = !_compacto && _animConsumo.Valor > 0.001f;
            _consumo.Visible = _ciclos.Visible = _lbCiclos.Visible = algo;
            _cabConsumo.Aberto = _animConsumo.Aberto;
            _cabConsumo.Invalidate();
        }

        public override int Tipo { get { return 1; } }

        /// <summary>Em ciclo, o tempo restante e a hora do fim andam com a hora da leitura.</summary>
        protected override string Assinatura()
        {
            CicloAtual c = CicloAtual.De(A);
            return base.Assinatura() + (c.Online && c.Rodando ? "|" + ServicoCasa.UltimaLeitura.ToString("HH:mm") : "");
        }
        protected override string Icone { get { return null; } }

        public CartaoDaLavadora()
        {
            _anel = new AnelDoCiclo(); Controls.Add(_anel);
            _etapas = new EtapasDoCiclo(); Controls.Add(_etapas);
            _titulo = Rotulo("", Ui.FontAppName, Ui.Text);
            _programa = Rotulo("", Ui.FontBase, Ui.Text);
            _termino = Rotulo("", Ui.FontBase, Ui.Muted);
            _aviso = Rotulo("", Ui.FontSmall, Ui.Warn);
            _lbParams = Rotulo(T.LavParametros, Ui.FontSmall, Ui.Muted);
            _lbCiclos = Rotulo(T.LavUltimosCiclos, Ui.FontSmall, Ui.Muted);
            _ciclos = Rotulo("", Ui.FontSmall, Ui.Text);
            _btTudo = new FlatBtn();
            _btTudo.Text = T.CasaTodas;
            _btTudo.Click += delegate { _aberto = !_aberto; Preencher(); AvisarAltura(); };
            Controls.Add(_btTudo);
            _tudo = Rotulo("", Ui.FontSmall, Ui.Muted);

            _chave = new BotaoChave();
            _chave.Click += delegate { AlternarDiagnostico(); };
            _dica.SetToolTip(_chave, T.CasaDiagnostico);
            Controls.Add(_chave);
            _lbDiag = Rotulo(T.CasaDiagnostico, Ui.FontMed, Ui.Text);
            _diag = Rotulo("", Ui.FontSmall, Ui.Text);
            _lbDiag.Visible = _diag.Visible = false;

            _cabConsumo = new CabecalhoRecolhivel();
            _cabConsumo.Titulo = T.LavConsumoTitulo;
            _cabConsumo.Click += delegate { AlternarConsumo(true); };
            Controls.Add(_cabConsumo);
            _consumo = Rotulo("", Ui.FontBase, Ui.Text);
            // Comeca fechada, como o consumo do ar, e lembra a escolha.
            _animConsumo = new AnimacaoDeRecolher(ServicoCasa.Pref.ConsumoLavadoraAberto);
            _animConsumo.Quadro += delegate { AjustarConsumo(); AvisarAltura(); };
            AjustarConsumo();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _animConsumo.Dispose(); _dica.Dispose(); }
            base.Dispose(disposing);
        }

        /// <summary>A Segoe nao tem lavadora: porta redonda numa caixa, desenhada.</summary>
        protected override void DesenharIcone(Graphics g, Rectangle r, Color cor)
        {
            IconesCasa.Lavadora(g, r, cor);
        }

        private void Parametros(List<string[]> l)
        {
            while (_params.Count < l.Count)
            {
                Label r = Rotulo("", Ui.FontSmall, Ui.Muted), v = Rotulo("", Ui.FontMed, Ui.Text);
                _params.Add(new[] { r, v });
            }
            for (int i = 0; i < _params.Count; i++)
            {
                bool vis = i < l.Count;
                _params[i][0].Visible = _params[i][1].Visible = vis;
                if (!vis) continue;
                _params[i][0].Text = l[i][0];
                _params[i][1].Text = l[i][1];
            }
        }

        protected override void Preencher()
        {
            CicloAtual c = CicloAtual.De(A);
            DateTime leitura = ServicoCasa.UltimaLeitura == DateTime.MinValue ? DateTime.Now : ServicoCasa.UltimaLeitura;

            _titulo.Text = c.Titulo();
            _titulo.ForeColor = !c.Online ? Ui.Muted : c.Erro ? Ui.Danger : Ui.Text;
            _programa.Text = c.Programa > 0 ? TabelaDaLavadora.Programa(c.Programa) : "";
            Color cor = c.Erro ? Ui.Danger : c.Pausada ? Ui.Warn : c.Concluido ? Color.FromArgb(0x2E, 0xA0, 0x43) : Ui.Accent;

            // --- anel: tempo restante e hora prevista do fim
            _anel.Cor = cor;
            _anel.Apagado = !c.Online || !c.Rodando;
            _anel.Progresso = c.Online ? c.Progresso : 0f;
            if (c.Online && c.Rodando && c.Restante > 0)
            {
                _anel.Grande = CiclosDaLavadora.Duracao(TimeSpan.FromMinutes(c.Restante));
                _anel.Pequeno = T.LavRestantes;
                DateTime termina = leitura.AddMinutes(c.Restante);
                _anel.Rodape = T.LavTerminaAs(termina.ToString("HH:mm"));
                _termino.Text = c.Programado ? T.LavProgramadoTermina(termina.ToString("HH:mm")) :
                    T.LavCicloDe(CiclosDaLavadora.Duracao(TimeSpan.FromMinutes(c.Total)), (int)Math.Round(c.Progresso * 100));
            }
            else
            {
                _anel.Grande = c.Concluido && c.Online ? "✓" : "--";
                _anel.Pequeno = c.Online ? (c.Concluido ? T.LavConcluida : T.LavSemCiclo) : T.LavDesconectadaCurto;
                _anel.Rodape = "";
                _termino.Text = !c.Online ? T.LavUltimaLeitura(leitura.ToString("HH:mm")) : "";
            }
            _anel.Invalidate();
            _programa.Visible = _programa.Text.Length > 0;
            _termino.Visible = _termino.Text.Length > 0;

            // --- etapas
            int atual = EtapasDoCiclo.Montar(A, c, _etapas.Nomes);
            _etapas.Atual = c.Online && (c.Rodando || c.Concluido) ? atual : -1;
            _etapas.Cor = cor;
            _etapas.Visible = c.Rodando || c.Concluido;
            _etapas.Invalidate();

            // --- parametros do ciclo
            List<string[]> p = new List<string[]>();
            if (A.Tem("temperature")) p.Add(new[] { T.CasaTemperatura, TabelaDaLavadora.Temperatura(A.Int("temperature", 0)) });
            if (A.Tem("Spin_speed_rpm")) p.Add(new[] { T.CasaCentrifugacao, TabelaDaLavadora.Centrifugacao(A.Int("Spin_speed_rpm", 0)) });
            if (A.Tem("RinseNum"))
            {
                int ex = A.Int("ExtraRinseNum", 0);
                p.Add(new[] { T.LavEnxagues, A.Int("RinseNum", 0) + (ex > 0 ? " + " + ex + " " + T.LavExtra : "") });
            }
            if (A.Tem("dry_time")) p.Add(new[] { T.LavSecagem, TabelaDaLavadora.Secagem(A.Int("dry_time", 0)) });
            if (A.Tem("Soil_Lever") && A.Int("SoilLeverFlag", 0) == 0) p.Add(new[] { T.LavSujeira, TabelaDaLavadora.Sujeira(A.Int("Soil_Lever", 0)) });
            int h = A.Int("DelayEndTime", 0), mm = A.Int("DelayEndTime_Minute", 0);
            if (h > 0 || mm > 0 || c.Programado) p.Add(new[] { T.LavProgramado, string.Format("{0} h {1:00}", h, mm) });
            string porta = A.Primeiro("Door_status", "DoorStatus");
            if (porta != null) p.Add(new[] { T.CasaPorta, TabelaDaLavadora.Porta(A.Int(porta, 0)) });
            string trava = A.Primeiro("Child_lock", "ChildLockActive", "Child_lock_setting_status");
            if (trava != null) p.Add(new[] { T.CasaTrava, A.Int(trava, 0) != 0 ? T.LavAtiva : T.LavDesativada });
            Parametros(p);

            // --- consumo do ciclo e do mes (secao recolhivel)
            List<string> cons = new List<string>();
            if (c.Agua.HasValue && (c.Agua.Value > 0 || c.Rodando)) cons.Add(T.LavAgua + ": " + c.Agua.Value.ToString("0.#") + " L");
            if (c.Energia.HasValue && (c.Energia.Value > 0 || c.Rodando)) cons.Add(T.LavEnergia + ": " + c.Energia.Value.ToString("0.00") + " kWh");
            double[] mes = CiclosDaLavadora.DoMes(A.Puid, DateTime.Today);
            cons.Add(T.LavMes((int)mes[0], mes[1].ToString("0"), mes[2].ToString("0.00")));
            _consumo.Text = string.Join("\n", cons.ToArray());
            _cabConsumo.Resumo = c.Agua.HasValue || c.Energia.HasValue
                ? T.LavResumoConsumo(c.Agua.HasValue ? c.Agua.Value.ToString("0") : "--", c.Energia.HasValue ? c.Energia.Value.ToString("0.00") : "--")
                : T.LavMesCurto((int)mes[0]);
            _cabConsumo.Invalidate();
            _lbParams.Visible = p.Count > 0;

            // --- avisos: limpeza do tambor e erro
            List<string> av = new List<string>();
            if (A.Int("DrumCleanFlag", 0) == 1) av.Add(T.LavLimparTambor);
            if (c.Erro && c.CodigoDeErro != 0) av.Add(string.Format(T.LavErroCodigo, c.CodigoDeErro));
            _aviso.Text = av.Count > 0 ? "⚠  " + string.Join("  ·  ", av.ToArray()) : "";
            _aviso.Visible = av.Count > 0;
            _chave.Falha = Diagnostico.TemFalha(A);
            _chave.Invalidate();
            if (_diagAberto) _diag.Text = Diagnostico.Texto(A);

            // --- ultimos ciclos registrados pelo Mhiagos
            List<CiclosDaLavadora.Ciclo> ult = CiclosDaLavadora.Ultimos(A.Puid, 5);
            List<string> linhas = new List<string>();
            foreach (CiclosDaLavadora.Ciclo x in ult)
                linhas.Add(x.Inicio.ToString("dd/MM HH:mm") + "  ·  " + TabelaDaLavadora.Programa(x.Programa) + "  ·  " +
                    CiclosDaLavadora.Duracao(x.Fim - x.Inicio) +
                    (x.Agua > 0 ? "  ·  " + x.Agua.ToString("0") + " L" : "") +
                    (x.Energia > 0 ? "  ·  " + x.Energia.ToString("0.00") + " kWh" : "") +
                    (x.Concluido ? "" : "  ·  " + T.LavInterrompido));
            _ciclos.Text = linhas.Count > 0 ? string.Join("\n", linhas.ToArray()) : T.LavSemCiclosGravados;

            _btTudo.Text = _aberto ? T.CasaMenos : T.CasaTodas;
            List<string> keys = new List<string>(A.Estado.Keys);
            keys.Sort(StringComparer.OrdinalIgnoreCase);
            List<string> todas = new List<string>();
            foreach (string k in keys) todas.Add(k + " = " + A.Estado[k]);
            _tudo.Text = string.Join("\n", todas.ToArray());
            _tudo.Visible = _aberto;
            _btTudo.Visible = A.Estado.Count > 0;

            // Fora de ciclo (parada, pronta ou desconectada) o cartao fica compacto: so o nome
            // no cabecalho e o estado. Anel, etapas, parametros e consumo so com ciclo rodando.
            _compacto = !(c.Online && c.Rodando);
            if (_compacto)
            {
                foreach (Control x in new Control[] { _anel, _programa, _termino, _etapas, _aviso, _lbParams, _btTudo, _tudo, _cabConsumo, _consumo, _ciclos, _lbCiclos })
                    x.Visible = false;
                foreach (Label[] l in _params) l[0].Visible = l[1].Visible = false;
            }
            else
            {
                _anel.Visible = _cabConsumo.Visible = true;
                AjustarConsumo();
            }
        }

        public override int Arranjar(int w)
        {
            int iw = w - 2 * M;
            int y0 = Cabeca + Ui.S(18);
            bool lado = iw >= Ui.S(600);
            int anel = Ui.S(200);

            // Chave do diagnostico no cabecalho, a esquerda do selo (como no ar).
            int seloW = 0;
            foreach (string s in new[] { T.CasaOnline, T.CasaOffline, T.CasaEnviando })
                seloW = Math.Max(seloW, TextRenderer.MeasureText(s, Ui.FontSmall).Width + Ui.S(22));
            int kc = Ui.S(32);
            _chave.SetBounds(w - M - seloW - Ui.S(10) - kc, (Cabeca - kc) / 2, kc, kc);
            if (_compacto)
            {
                int yk = Bloco(_titulo, M, Cabeca + Ui.S(12), iw);
                if (_diag.Visible)
                {
                    _lbDiag.SetBounds(M, yk + Ui.S(10), iw, Ui.S(22));
                    yk = Bloco(_diag, M, yk + Ui.S(36), iw);
                }
                return yk + Ui.S(14);
            }
            int ax = lado ? M : M + (iw - anel) / 2;
            _anel.SetBounds(ax, y0, anel, anel);

            int cx = lado ? M + anel + Ui.S(28) : M;
            int cw = lado ? w - M - cx : iw;
            int y = lado ? y0 + Ui.S(10) : y0 + anel + Ui.S(12);
            y = Bloco(_titulo, cx, y, cw) + Ui.S(2);
            if (_programa.Visible) y = Bloco(_programa, cx, y, cw) + Ui.S(2);
            if (_termino.Visible) y = Bloco(_termino, cx, y, cw) + Ui.S(10);
            if (_etapas.Visible) { _etapas.SetBounds(cx, y + Ui.S(6), cw, Ui.S(48)); y += Ui.S(60); }
            if (_aviso.Visible) y = Bloco(_aviso, cx, y + Ui.S(4), cw) + Ui.S(4);
            y = Math.Max(y, lado ? y0 + anel : y) + Ui.S(14);

            // Parametros em grade: rotulo pequeno em cima, valor embaixo.
            if (_lbParams.Visible)
            {
                _lbParams.SetBounds(M, y, iw, Ui.S(18));
                y += Ui.S(24);
                int vis = 0;
                foreach (Label[] l in _params) if (l[0].Visible) vis++;
                int col = Math.Max(1, Math.Min(vis, iw / Ui.S(150)));
                int colW = iw / col;
                int i = 0;
                foreach (Label[] l in _params)
                {
                    if (!l[0].Visible) continue;
                    int px = M + (i % col) * colW, py = y + (i / col) * Ui.S(46);
                    l[0].SetBounds(px, py, colW - Ui.S(8), Ui.S(18));
                    l[1].SetBounds(px, py + Ui.S(18), colW - Ui.S(8), Ui.S(22));
                    i++;
                }
                y += ((i + col - 1) / col) * Ui.S(46) + Ui.S(8);
            }

            if (_diag.Visible)
            {
                _lbDiag.SetBounds(M, y, iw, Ui.S(22));
                y = Bloco(_diag, M, y + Ui.S(26), iw) + Ui.S(14);
            }

            if (_btTudo.Visible)
            {
                int bw = TextRenderer.MeasureText(_btTudo.Text, _btTudo.Font).Width + Ui.S(28);
                _btTudo.SetBounds(M, y, bw, Ui.S(30));
                y += Ui.S(40);
            }
            if (_tudo.Visible) y = Bloco(_tudo, M, y, iw) + Ui.S(6);

            // Consumo e historico: cabecalho sempre visivel, conteudo animado no fim do cartao.
            _cabConsumo.SetBounds(M, y, iw, Ui.S(38));
            y += Ui.S(38);
            int topo = y + Ui.S(8);
            int yc = Bloco(_consumo, M, topo, iw) + Ui.S(10);
            _lbCiclos.SetBounds(M, yc, iw, Ui.S(18));
            yc += Ui.S(22);
            yc = Bloco(_ciclos, M, yc, iw) + Ui.S(6);
            y += (int)Math.Round((yc - y) * _animConsumo.Valor);
            return y + Ui.S(10);
        }

        private static int Bloco(Label l, int x, int y, int w)
        {
            Size s = TextRenderer.MeasureText(l.Text.Length > 0 ? l.Text : " ", l.Font, new Size(w, 0), TextFormatFlags.WordBreak);
            l.SetBounds(x, y, w, s.Height + Ui.S(2));
            return y + s.Height + Ui.S(2);
        }
    }

    public static partial class T
    {
        public static string LavProgramaN { get { return P("Programa {0}", "Program {0}"); } }
        public static string LavProgramaProvavel { get { return P("Programa {0} (provável: {1})", "Program {0} (likely: {1})"); } }
        public static string LavConsumoTitulo { get { return P("Consumo e histórico", "Usage and history"); } }
        public static string LavResumoConsumo(string agua, string kwh) { return P("ciclo: " + agua + " L · " + kwh + " kWh", "cycle: " + agua + " L · " + kwh + " kWh"); }
        public static string LavMesCurto(int n) { return P(n + " ciclos este mês", n + " cycles this month"); }
        public static string LavMes(int n, string agua, string kwh) { return P("Este mês (ciclos registrados pelo Mhiagos): " + n + "  ·  " + agua + " L  ·  " + kwh + " kWh", "This month (cycles recorded by Mhiagos): " + n + "  ·  " + agua + " L  ·  " + kwh + " kWh"); }
        public static string LavSemPrograma { get { return P("Sem programa", "No program"); } }
        public static string LavFasePesando { get { return P("Pesando a roupa", "Weighing"); } }
        public static string LavFasePrelavagem { get { return P("Pré-lavagem", "Prewash"); } }
        public static string LavFaseLavagem { get { return P("Lavando", "Washing"); } }
        public static string LavFaseEnxague { get { return P("Enxaguando", "Rinsing"); } }
        public static string LavFaseAmaciante { get { return P("Amaciando", "Softening"); } }
        public static string LavFaseAntirrugas { get { return P("Antirrugas", "Anti-crease"); } }
        public static string LavFaseCentrifugacao { get { return P("Centrifugando", "Spinning"); } }
        public static string LavFaseSecagem { get { return P("Secando", "Drying"); } }
        public static string LavFaseArejando { get { return P("Arejando", "Airing"); } }
        public static string LavFaseFim { get { return P("Fim do programa", "Program finished"); } }
        public static string LavFaseProgramado { get { return P("Aguardando o início programado", "Waiting for delayed start"); } }
        public static string LavPortaDestravada { get { return P("destravada", "unlocked"); } }
        public static string LavPortaTravada { get { return P("travada", "locked"); } }
        public static string LavPortaFechada { get { return P("fechada", "closed"); } }
        public static string LavFria { get { return P("Fria", "Cold"); } }
        public static string LavSemCentrifugar { get { return P("Sem centrifugar", "No spin"); } }
        public static string LavSemSecagem { get { return P("Sem secagem", "No drying"); } }
        public static string LavSecaPassar { get { return P("Para passar", "Iron dry"); } }
        public static string LavSecaVestir { get { return P("Para vestir", "Wear dry"); } }
        public static string LavSecaGuardar { get { return P("Para guardar", "Cupboard dry"); } }
        public static string LavSecaIntensa { get { return P("Intensa", "Extra dry"); } }
        public static string LavSujeiraNormal { get { return P("Normal", "Standard"); } }
        public static string LavSujeiraLeve { get { return P("Leve", "Light"); } }
        public static string LavSujeiraMedia { get { return P("Média", "Medium"); } }
        public static string LavSujeiraPesada { get { return P("Pesada", "Heavy"); } }
        public static string LavDesconectada { get { return P("Desconectada da nuvem", "Offline from the cloud"); } }
        public static string LavDesconectadaCurto { get { return P("desconectada", "offline"); } }
        public static string LavErro { get { return P("Erro: veja o painel da máquina", "Error: check the machine"); } }
        public static string LavErroCodigo { get { return P("Erro {0}: veja o painel da máquina", "Error {0}: check the machine"); } }
        public static string LavPausada { get { return P("Pausada", "Paused"); } }
        public static string LavEmFuncionamento { get { return P("Em funcionamento", "Running"); } }
        public static string LavConcluida { get { return P("Ciclo concluído", "Cycle finished"); } }
        public static string LavLigada { get { return P("Ligada, sem ciclo", "On, no cycle"); } }
        public static string LavEmEspera { get { return P("Em espera", "Standby"); } }
        public static string LavRestantes { get { return P("restantes", "left"); } }
        public static string LavTerminaAs(string h) { return P("termina às " + h, "ends at " + h); }
        public static string LavProgramadoTermina(string h) { return P("Início programado; termina às " + h, "Delayed start; ends at " + h); }
        public static string LavCicloDe(string total, int pct) { return P("Ciclo de " + total + "  ·  " + pct + "% concluído", total + " cycle  ·  " + pct + "% done"); }
        public static string LavSemCiclo { get { return P("sem ciclo", "no cycle"); } }
        public static string LavUltimaLeitura(string h) { return P("Última leitura conhecida às " + h + ". Os valores abaixo podem estar desatualizados.", "Last known reading at " + h + ". The values below may be out of date."); }
        public static string LavEtapaLavar { get { return P("Lavar", "Wash"); } }
        public static string LavEtapaEnxaguar { get { return P("Enxaguar", "Rinse"); } }
        public static string LavEtapaCentrifugar { get { return P("Centrifugar", "Spin"); } }
        public static string LavEtapaSecar { get { return P("Secar", "Dry"); } }
        public static string LavEtapaFim { get { return P("Pronto", "Done"); } }
        public static string LavParametros { get { return P("Este ciclo", "This cycle"); } }
        public static string LavEnxagues { get { return P("Enxágues", "Rinses"); } }
        public static string LavExtra { get { return P("a mais", "extra"); } }
        public static string LavSecagem { get { return P("Secagem", "Drying"); } }
        public static string LavSujeira { get { return P("Sujeira", "Soil level"); } }
        public static string LavProgramado { get { return P("Fim programado", "Delayed end"); } }
        public static string LavAtiva { get { return P("ativa", "on"); } }
        public static string LavDesativada { get { return P("desativada", "off"); } }
        public static string LavAgua { get { return P("Água (ciclo)", "Water (cycle)"); } }
        public static string LavEnergia { get { return P("Energia (ciclo)", "Energy (cycle)"); } }
        public static string LavLimparTambor { get { return P("A máquina pede a limpeza do tambor", "The machine asks for a drum clean"); } }
        public static string LavUltimosCiclos { get { return P("Últimos ciclos (registrados pelo Mhiagos)", "Recent cycles (recorded by Mhiagos)"); } }
        public static string LavSemCiclosGravados { get { return P("Nenhum ciclo registrado ainda. Os ciclos passam a ser gravados com o Mhiagos rodando.", "No cycles recorded yet. Cycles are recorded while Mhiagos is running."); } }
        public static string LavInterrompido { get { return P("interrompido", "interrupted"); } }
        public static string LavAvisoFimTitulo(string nome) { return P(nome + ": ciclo concluído", nome + ": cycle finished"); }
        public static string LavAvisoFimTexto(string prog, string dur) { return P(prog + " terminou depois de " + dur + ". Pode tirar a roupa.", prog + " finished after " + dur + ". Laundry is ready."); }
        public static string LavAvisoErroTitulo(string nome) { return P(nome + ": erro", nome + ": error"); }
        public static string CasaAlertaLavadoraFimOpcao { get { return P("Lavadora: ciclo concluído", "Washer: cycle finished"); } }
    }
}
