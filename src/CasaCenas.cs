using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// Uma cena do ar: o que ajustar num clique. -1 em qualquer campo e "manter
    /// como esta". Temperatura sempre em °C; vira °F na hora de mandar, se o
    /// aparelho estiver em °F. Ventilador: -2 e o minimo do modelo, -3 o maximo.
    /// </summary>
    internal sealed class CenaCasa
    {
        public string Nome = "", Icone = "floco";
        public bool Desligar;
        public int Modo = -1, Temp = -1, Vento = -1, Noturno = -1;
        /// <summary>Minutos ate voltar ao padrao (24 °C, Frio, ventilador baixo); 0 nao volta.</summary>
        public int Duracao;
        public static readonly int[] Duracoes = { 0, 15, 30, 60, 120 };

        public const int VentoMinimo = -2, VentoMaximo = -3;
        public const int MaxCenas = 8;

        public CenaCasa Copia()
        {
            CenaCasa c = new CenaCasa();
            c.Nome = Nome; c.Icone = Icone; c.Desligar = Desligar;
            c.Modo = Modo; c.Temp = Temp; c.Vento = Vento; c.Noturno = Noturno; c.Duracao = Duracao;
            return c;
        }

        /// <summary>As tres de fabrica: a semente e o "Restaurar padroes".</summary>
        public static List<CenaCasa> Padrao()
        {
            CenaCasa dormir = new CenaCasa();
            dormir.Nome = T.CasaCenaDormir; dormir.Icone = "lua"; dormir.Temp = 24; dormir.Vento = VentoMinimo; dormir.Noturno = 1;
            CenaCasa gelar = new CenaCasa();
            gelar.Nome = T.CasaCenaGelar; gelar.Icone = "floco"; gelar.Modo = 2; gelar.Temp = 18; gelar.Vento = VentoMaximo; gelar.Noturno = 0;
            CenaCasa eco = new CenaCasa();
            eco.Nome = T.CasaCenaEco; eco.Icone = "folha"; eco.Modo = 5; eco.Temp = 25;
            return new List<CenaCasa> { dormir, gelar, eco };
        }

        // ---------------- gravacao (uma linha por cena em casa.ini) ----------------

        public string Linha()
        {
            string nome = (Nome ?? "").Replace("|", "/").Replace("\r", " ").Replace("\n", " ").Trim();
            return string.Join("|", new[] { nome, Icone ?? "floco", Desligar ? "1" : "0",
                N(Modo), N(Temp), N(Vento), N(Noturno), N(Duracao) });
        }

        private static string N(int v) { return v.ToString(CultureInfo.InvariantCulture); }

        public static CenaCasa DeLinha(string s)
        {
            string[] c = (s ?? "").Split('|');
            if (c.Length < 7 || c[0].Trim().Length == 0) return null;
            CenaCasa r = new CenaCasa();
            r.Nome = c[0].Trim();
            r.Icone = IconesDeCena.Existe(c[1]) ? c[1] : "floco";
            r.Desligar = c[2] == "1";
            int.TryParse(c[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out r.Modo);
            int.TryParse(c[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out r.Temp);
            int.TryParse(c[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out r.Vento);
            int.TryParse(c[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out r.Noturno);
            // 8o campo desde o item 3; linhas antigas, de 7, ficam sem retorno.
            if (c.Length > 7) int.TryParse(c[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out r.Duracao);
            if (r.Duracao < 0) r.Duracao = 0;
            return r;
        }

        // ---------------- comando ----------------

        internal static bool Aceita(Aparelho a, string chave, int valor)
        {
            if (a == null) return false;
            MetaPropriedade m;
            if (a.Meta == null || !a.Meta.TryGetValue(chave, out m)) return a.Tem(chave);
            if (!m.Grava) return false;
            if (m.Valores.Count > 0) return m.Valores.Contains(valor);
            foreach (int[] f in m.Faixas) if (valor >= f[0] && valor <= f[1]) return true;
            return m.Faixas.Count == 0;
        }

        /// <summary>Velocidades do modelo, sem o Auto (0), em ordem.</summary>
        internal static List<int> Velocidades(Aparelho a)
        {
            List<int> v = new List<int>();
            MetaPropriedade m;
            if (a != null && a.Meta != null && a.Meta.TryGetValue("t_fan_speed", out m)) v.AddRange(m.Valores);
            v.Sort();
            return v;
        }

        /// <summary>Faixa de temperatura do modelo em °C.</summary>
        internal static void FaixaC(Aparelho a, out int min, out int max)
        {
            min = 16; max = 30;
            MetaPropriedade m;
            if (a != null && a.Meta != null && a.Meta.TryGetValue("t_temp", out m) && m.Faixas.Count > 0)
            { min = m.Faixas[0][0]; max = m.Faixas[0][1]; }
        }

        private int VentoResolvido(Aparelho a)
        {
            if (Vento >= 0) return Vento;
            List<int> v = Velocidades(a);
            v.Remove(0);
            if (v.Count == 0) return -1;
            return Vento == VentoMinimo ? v[0] : Vento == VentoMaximo ? v[v.Count - 1] : -1;
        }

        /// <summary>
        /// As propriedades da cena para este aparelho, ou nulo se o modelo nao
        /// aceita alguma escolha explicita dela. A divisao do noturno num segundo
        /// comando acontece no envio (ServicoCasa.Partes).
        /// </summary>
        public Dictionary<string, int> Comando(Aparelho a)
        {
            if (a == null) return null;
            Dictionary<string, int> p = new Dictionary<string, int>();
            if (Desligar)
            {
                if (!Aceita(a, "t_power", 0)) return null;
                p["t_power"] = 0;
                return p;
            }
            if (Aceita(a, "t_power", 1)) p["t_power"] = 1;
            if (Modo >= 0)
            {
                if (!Aceita(a, "t_work_mode", Modo)) return null;
                p["t_work_mode"] = Modo;
            }
            if (Temp > 0)
            {
                int min, max;
                FaixaC(a, out min, out max);
                int c = Math.Max(min, Math.Min(max, Temp));
                bool f = a.Int("t_temp_type", 0) == 1;
                int t = f ? (int)Math.Round(c * 9.0 / 5 + 32) : c;
                if (!Aceita(a, "t_temp", t)) return null;
                p["t_temp"] = t;
            }
            if (Vento != -1)
            {
                int v = VentoResolvido(a);
                if (v >= 0 && Aceita(a, "t_fan_speed", v)) p["t_fan_speed"] = v;
                else if (Vento >= 0) return null;
            }
            if (Noturno >= 0)
            {
                if (!Aceita(a, "t_sleep", Noturno)) return null;
                p["t_sleep"] = Noturno;
            }
            return p.Count > 0 ? p : null;
        }

        /// <summary>"Frio · 18° · Alta · noturno" - o que a cena faz, em poucas palavras.</summary>
        public string Resumo(Aparelho a)
        {
            if (Desligar) return T.CasaCenaSoDesliga;
            List<string> l = new List<string>();
            if (Modo >= 0) l.Add(TabelaDoAr.Modo(Modo, a != null ? a.Tipo : "008"));
            if (Temp > 0) l.Add(Temp + "°C");
            if (Vento != -1)
            {
                int v = VentoResolvido(a);
                l.Add(v >= 0 ? TabelaDoAr.Ventilador(v, a != null ? a.Tipo : "008")
                    : Vento == VentoMinimo ? T.CasaVentoMinimo : T.CasaVentoMaximo);
            }
            if (Noturno == 1) l.Add(T.CasaNoturnoCurto);
            if (Noturno == 0) l.Add(T.CasaSemNoturnoCurto);
            if (Duracao > 0) l.Add(T.CasaPorMinutos(Duracao));
            return l.Count > 0 ? string.Join(" · ", l.ToArray()) : T.CasaCenaSoLiga;
        }
    }

    /// <summary>Os icones que uma cena pode ter, pelo nome gravado.</summary>
    internal static class IconesDeCena
    {
        public static readonly string[] Nomes = { "floco", "lua", "folha", "ventoinha", "gota", "sol", "energia" };

        public static bool Existe(string n) { return Array.IndexOf(Nomes, n) >= 0; }

        public static Action<Graphics, RectangleF, Color> Desenho(string n)
        {
            switch (n)
            {
                case "lua": return IconesCasa.Lua;
                case "folha": return IconesCasa.Folha;
                case "ventoinha": return IconesCasa.Ventoinha;
                case "gota": return IconesCasa.Gota;
                case "sol": return IconesCasa.Sol;
                case "energia": return IconesCasa.Energia;
                default: return IconesCasa.Floco;
            }
        }
    }

    /// <summary>
    /// Bloco de secao: fundo levemente destacado, titulo no alto e, opcional,
    /// um botao de acao a direita do titulo. Quem usa posiciona os filhos a
    /// partir de Topo, dentro de Margem.
    /// </summary>
    internal sealed class Secao : Control
    {
        public string Titulo;
        public readonly FlatBtn Acao;
        public static readonly int Margem = Ui.S(14);
        public static readonly int Topo = Ui.S(44);

        public Secao(string titulo)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Titulo = titulo;
            Acao = new FlatBtn();
            Acao.Visible = false;
            Acao.Font = Ui.FontSmall;
            Controls.Add(Acao);
        }

        /// <summary>Largura util para o conteudo.</summary>
        public int Util { get { return Width - 2 * Margem; } }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Acao == null || !Acao.Visible) return;
            int w = TextRenderer.MeasureText(Acao.Text, Acao.Font).Width + Ui.S(24);
            Acao.SetBounds(Width - Margem - w, Ui.S(9), w, Ui.S(28));
        }

        public void PosicionarAcao() { OnResize(EventArgs.Empty); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(Ui.Surface)) g.FillRectangle(b, ClientRectangle);
            Ui.Smooth(g);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Ui.RoundRect(r, Ui.S(10)))
            using (SolidBrush b = new SolidBrush(Ui.Misturar(Ui.Surface, Ui.SurfaceAlt, 0.55f)))
            using (Pen pen = new Pen(Ui.Border))
            {
                g.FillPath(b, p);
                g.DrawPath(pen, p);
            }
            TextRenderer.DrawText(g, Titulo ?? "", Ui.FontMed, new Rectangle(Margem, Ui.S(12), Width - 2 * Margem, Ui.S(22)), Ui.Text,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>
    /// Os botoes das cenas. Clique roda a cena; no modo de edicao, abre o
    /// editor dela, e um bloco a mais cria cena nova.
    /// </summary>
    internal sealed class PainelDeCenas : Control
    {
        public event EventHandler<CenaEventArgs> Rodar;
        public event Action<int> Editar;   // -1 = nova
        public bool Editando;
        private Aparelho _a;
        private List<CenaCasa> _cenas = new List<CenaCasa>();
        private readonly List<Rectangle> _caixas = new List<Rectangle>();
        private int _hover = -1, _selecionada = -1;

        public PainelDeCenas()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
        }

        public int Selecionada { get { return _selecionada; } set { _selecionada = value; Invalidate(); } }

        public void Mostrar(Aparelho a, List<CenaCasa> cenas)
        {
            _a = a;
            _cenas = cenas;
            Invalidate();
        }

        private int Itens { get { return _cenas.Count + (Editando && _cenas.Count < CenaCasa.MaxCenas ? 1 : 0); } }

        private int Colunas(int w) { return Math.Max(1, Math.Min(3, (w + Ui.S(8)) / (Ui.S(150) + Ui.S(8)))); }

        /// <summary>Altura para a largura dada.</summary>
        public int Altura(int w)
        {
            int col = Colunas(w);
            int lin = Math.Max(1, (Itens + col - 1) / col);
            return lin * Ui.S(64) + (lin - 1) * Ui.S(8);
        }

        private void Caixas()
        {
            _caixas.Clear();
            int col = Colunas(Width);
            int bw = (Width - (col - 1) * Ui.S(8)) / col;
            for (int i = 0; i < Itens; i++)
                _caixas.Add(new Rectangle((i % col) * (bw + Ui.S(8)), (i / col) * Ui.S(72), bw - 1, Ui.S(64) - 1));
        }

        private int Hit(Point p) { Caixas(); for (int i = 0; i < _caixas.Count; i++) if (_caixas[i].Contains(p)) return i; return -1; }

        protected override void OnMouseMove(MouseEventArgs e) { int h = Hit(e.Location); if (h != _hover) { _hover = h; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int h = Hit(e.Location);
            if (h >= 0 && Enabled) Acionar(h);
            base.OnMouseDown(e);
        }

        private void Acionar(int i)
        {
            if (Editando)
            {
                if (Editar != null) Editar(i < _cenas.Count ? i : -1);
                return;
            }
            if (i >= _cenas.Count) return;
            Dictionary<string, int> p = _cenas[i].Comando(_a);
            if (p != null && Rodar != null) Rodar(this, new CenaEventArgs(p, _cenas[i]));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(Ui.Misturar(Ui.Surface, Ui.SurfaceAlt, 0.55f))) g.FillRectangle(b, ClientRectangle);
            Ui.Smooth(g);
            Caixas();
            for (int i = 0; i < _caixas.Count; i++)
            {
                Rectangle r = _caixas[i];
                bool nova = i >= _cenas.Count;
                bool disponivel = nova || Editando || _cenas[i].Comando(_a) != null;
                bool sel = Editando && i == _selecionada;
                Color fundo = sel ? Ui.AccentSoft : i == _hover && disponivel ? Ui.Hover : Ui.Surface;
                using (GraphicsPath p = Ui.RoundRect(r, Ui.S(10)))
                {
                    using (SolidBrush b = new SolidBrush(fundo)) g.FillPath(b, p);
                    using (Pen pen = new Pen(sel ? Ui.Accent : Ui.Border, sel ? 2f : 1f))
                    {
                        if (nova) pen.DashPattern = new float[] { 3, 3 };
                        g.DrawPath(pen, p);
                    }
                }
                Color frente = disponivel ? Ui.Text : Ui.Faint;
                if (nova)
                {
                    TextRenderer.DrawText(g, "+  " + T.CasaNovaCena, Ui.FontMed, r, Ui.AccentText,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    continue;
                }
                CenaCasa c = _cenas[i];
                Rectangle ic = new Rectangle(r.X + Ui.S(12), r.Y + (r.Height - Ui.S(34)) / 2, Ui.S(34), Ui.S(34));
                using (SolidBrush b = new SolidBrush(disponivel ? Ui.AccentSoft : Ui.SurfaceAlt)) g.FillEllipse(b, ic);
                IconesDeCena.Desenho(c.Icone)(g, RectangleF.Inflate(ic, -Ui.S(9), -Ui.S(9)), disponivel ? Ui.AccentText : Ui.Faint);
                int tx = ic.Right + Ui.S(10);
                int tw = r.Right - tx - (Editando ? Ui.S(26) : Ui.S(8));
                TextRenderer.DrawText(g, c.Nome, Ui.FontMed, new Rectangle(tx, r.Y + Ui.S(11), tw, Ui.S(22)), frente,
                    TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                string resumo = disponivel || Editando ? c.Resumo(_a) : T.CasaCenaIndisponivel;
                TextRenderer.DrawText(g, resumo, Ui.FontSmall, new Rectangle(tx, r.Y + Ui.S(33), tw, Ui.S(18)), Ui.Muted,
                    TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                if (Editando)
                    TextRenderer.DrawText(g, "\uE70F", Ui.FontGlyph9, new Rectangle(r.Right - Ui.S(28), r.Y, Ui.S(22), r.Height), Ui.Muted,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            if (Focused && ShowFocusCues && _caixas.Count > 0)
                using (Pen p = new Pen(Ui.Accent, 2f)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }
    }

    /// <summary>
    /// Editor de uma cena: nome, icone, liga ou desliga, modo, temperatura,
    /// ventilador e noturno - so com o que o modelo aceita. "Manter" deixa o
    /// campo como estiver no aparelho.
    /// </summary>
    internal sealed class EditorDeCena : Control
    {
        public event EventHandler Salvar, Cancelar, Excluir, Antes, Depois;
        private readonly Label _lbNome, _lbIcone, _lbAcao, _lbModo, _lbTemp, _lbVento, _lbNoturno;
        private readonly CampoDeTexto _nome;
        private readonly Blocos _icone, _modo, _vento;
        private readonly Segmented _acao, _noturno, _duracao;
        private readonly Label _lbDuracao;
        private readonly Toggle _ajustaTemp;
        private readonly Label _grau;
        private readonly NumberBox _temp;
        private readonly FlatBtn _salvar, _cancelar, _excluir, _antes, _depois;
        private Aparelho _a;
        public bool Nova;

        public EditorDeCena()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            _lbNome = Rotulo(T.CasaCenaNome);
            _nome = new CampoDeTexto(); _nome.Icone = null; Controls.Add(_nome);
            _lbIcone = Rotulo(T.CasaCenaIcone);
            _icone = new Blocos();
            foreach (string n in IconesDeCena.Nomes)
            {
                Blocos.Item it = new Blocos.Item();
                it.Valor = Array.IndexOf(IconesDeCena.Nomes, n); it.Rotulo = ""; it.Icone = IconesDeCena.Desenho(n);
                _icone.Itens.Add(it);
            }
            Controls.Add(_icone);

            _lbAcao = Rotulo(T.CasaCenaAcao);
            _acao = new Segmented();
            _acao.SetItems(T.CasaCenaLigarAjustar, T.CasaCenaSoDesliga);
            _acao.SelectedIndexChanged += delegate { Habilitar(); };
            Controls.Add(_acao);

            _lbModo = Rotulo(T.CasaModo);
            _modo = new Blocos(); Controls.Add(_modo);
            _lbTemp = Rotulo(T.CasaTemperatura);
            _ajustaTemp = new Toggle(); _ajustaTemp.Label = T.CasaCenaAjustar;
            _ajustaTemp.CheckedChanged += delegate { _temp.Enabled = _ajustaTemp.Checked; };
            Controls.Add(_ajustaTemp);
            _temp = new NumberBox(); Controls.Add(_temp);
            _grau = Rotulo("°C");
            _grau.ForeColor = Ui.Text;
            _lbVento = Rotulo(T.CasaVentilador);
            _vento = new Blocos(); Controls.Add(_vento);
            _lbNoturno = Rotulo(T.CasaFuncao("t_sleep"));
            _noturno = new Segmented();
            _noturno.SetItems(T.CasaManter, T.CasaLigar, T.CasaDesligarCurto);
            Controls.Add(_noturno);

            _lbDuracao = Rotulo(T.CasaVoltarDepois);
            _duracao = new Segmented();
            _duracao.SetItems(T.CasaNunca, "15 min", "30 min", "1 h", "2 h");
            Controls.Add(_duracao);

            _salvar = Botao(T.Save, true, false, delegate { if (Salvar != null) Salvar(this, EventArgs.Empty); });
            _cancelar = Botao(T.Cancel, false, false, delegate { if (Cancelar != null) Cancelar(this, EventArgs.Empty); });
            _antes = Botao("◀  " + T.CasaMoverAntes, false, false, delegate { if (Antes != null) Antes(this, EventArgs.Empty); });
            _depois = Botao(T.CasaMoverDepois + "  ▶", false, false, delegate { if (Depois != null) Depois(this, EventArgs.Empty); });
            _excluir = Botao(T.CasaExcluir, false, true, delegate { if (Excluir != null) Excluir(this, EventArgs.Empty); });
        }

        private Label Rotulo(string t)
        {
            Label l = new Label();
            l.Text = t; l.Font = Ui.FontSmall; l.ForeColor = Ui.Muted; l.AutoSize = false; l.BackColor = Color.Transparent;
            Controls.Add(l);
            return l;
        }

        private FlatBtn Botao(string t, bool primario, bool perigo, EventHandler clique)
        {
            FlatBtn b = new FlatBtn();
            b.Text = t; b.Primary = primario; b.Danger = perigo;
            b.Click += clique;
            Controls.Add(b);
            return b;
        }

        private static Blocos.Item Item(int valor, string rotulo, Action<Graphics, RectangleF, Color> icone)
        {
            Blocos.Item it = new Blocos.Item();
            it.Valor = valor; it.Rotulo = rotulo; it.Icone = icone;
            return it;
        }

        /// <summary>Carrega a cena nos campos, com as opcoes que o aparelho aceita.</summary>
        public void Carregar(CenaCasa c, Aparelho a, bool podeMover)
        {
            _a = a;
            _nome.Texto = c.Nome;
            _icone.Selecionado = Math.Max(0, Array.IndexOf(IconesDeCena.Nomes, c.Icone));
            _acao.SelectedIndex = c.Desligar ? 1 : 0;

            _modo.Itens.Clear();
            _modo.Itens.Add(Item(-1, T.CasaManter, IconesCasa.Manter));
            MetaPropriedade m;
            if (a != null && a.Meta != null && a.Meta.TryGetValue("t_work_mode", out m))
                foreach (int v in m.Valores) _modo.Itens.Add(Item(v, TabelaDoAr.Modo(v, a.Tipo), TabelaDoAr.IconeDoModo(v, a.Tipo)));
            _modo.Selecionado = c.Modo;
            _modo.Visible = _lbModo.Visible = _modo.Itens.Count > 1;

            int min, max;
            CenaCasa.FaixaC(a, out min, out max);
            _temp.Minimum = min; _temp.Maximum = max;
            _temp.Value = c.Temp > 0 ? Math.Max(min, Math.Min(max, c.Temp)) : 24;
            _ajustaTemp.Checked = c.Temp > 0;
            _temp.Enabled = c.Temp > 0;

            _vento.Itens.Clear();
            _vento.Itens.Add(Item(-1, T.CasaManter, IconesCasa.Manter));
            List<int> vs = CenaCasa.Velocidades(a);
            foreach (int v in vs) _vento.Itens.Add(Item(v, TabelaDoAr.Ventilador(v, a != null ? a.Tipo : "008"), null));
            int vento = c.Vento;
            List<int> sem0 = new List<int>(vs); sem0.Remove(0);
            if (vento == CenaCasa.VentoMinimo && sem0.Count > 0) vento = sem0[0];
            if (vento == CenaCasa.VentoMaximo && sem0.Count > 0) vento = sem0[sem0.Count - 1];
            _vento.Selecionado = vento;
            _vento.Visible = _lbVento.Visible = _vento.Itens.Count > 1;

            bool temNoturno = CenaCasa.Aceita(a, "t_sleep", 1);
            _noturno.SelectedIndex = c.Noturno < 0 ? 0 : c.Noturno == 1 ? 1 : 2;
            _noturno.Visible = _lbNoturno.Visible = temNoturno;

            _duracao.SelectedIndex = Math.Max(0, Array.IndexOf(CenaCasa.Duracoes, c.Duracao));
            _excluir.Visible = !Nova;
            _antes.Visible = _depois.Visible = podeMover && !Nova;
            Habilitar();
        }

        private void Habilitar()
        {
            bool ajusta = _acao.SelectedIndex == 0;
            foreach (Control c in new Control[] { _modo, _ajustaTemp, _vento, _noturno, _duracao }) c.Enabled = ajusta;
            _temp.Enabled = ajusta && _ajustaTemp.Checked;
        }

        /// <summary>A cena como esta nos campos.</summary>
        public CenaCasa Ler()
        {
            CenaCasa c = new CenaCasa();
            c.Nome = _nome.Texto.Trim().Length > 0 ? _nome.Texto.Trim() : T.CasaNovaCena;
            c.Icone = IconesDeCena.Nomes[Math.Max(0, Math.Min(IconesDeCena.Nomes.Length - 1, _icone.Selecionado))];
            c.Desligar = _acao.SelectedIndex == 1;
            if (c.Desligar) return c;
            c.Modo = _modo.Visible ? _modo.Selecionado : -1;
            c.Temp = _ajustaTemp.Checked ? _temp.Value : -1;
            c.Vento = _vento.Visible ? _vento.Selecionado : -1;
            c.Noturno = _noturno.Visible ? (_noturno.SelectedIndex == 0 ? -1 : _noturno.SelectedIndex == 1 ? 1 : 0) : -1;
            c.Duracao = CenaCasa.Duracoes[Math.Max(0, Math.Min(CenaCasa.Duracoes.Length - 1, _duracao.SelectedIndex))];
            return c;
        }

        /// <summary>Grade de rotulo a esquerda e campo a direita; uma coluna de rotulos.</summary>
        public int Arranjar(int w)
        {
            int lw = Math.Min(Ui.S(150), w / 3);
            int cx = lw + Ui.S(12), cw = w - cx;
            int y = 0;
            Linha(_lbNome, _nome, ref y, lw, cx, Math.Min(cw, Ui.S(320)), Ui.S(34));
            Linha(_lbIcone, _icone, ref y, lw, cx, Math.Min(cw, Ui.S(7 * 44 + 6 * 8)), Ui.S(40));
            Linha(_lbAcao, _acao, ref y, lw, cx, Math.Min(cw, Ui.S(360)), Ui.S(32));
            if (_modo.Visible) Linha(_lbModo, _modo, ref y, lw, cx, cw, Ui.S(40));
            _lbTemp.SetBounds(0, y + Ui.S(8), lw, Ui.S(18));
            _ajustaTemp.SetBounds(cx, y, Ui.S(130), Ui.S(32));
            _temp.SetBounds(cx + Ui.S(140), y, Ui.S(110), Ui.S(32));
            _grau.SetBounds(_temp.Right + Ui.S(8), y + Ui.S(7), Ui.S(30), Ui.S(18));
            y += Ui.S(32) + Ui.S(12);
            if (_vento.Visible) Linha(_lbVento, _vento, ref y, lw, cx, cw, Ui.S(40));
            if (_noturno.Visible) Linha(_lbNoturno, _noturno, ref y, lw, cx, Math.Min(cw, Ui.S(360)), Ui.S(32));
            Linha(_lbDuracao, _duracao, ref y, lw, cx, Math.Min(cw, Ui.S(420)), Ui.S(32));

            y += Ui.S(6);
            int x = 0;
            foreach (FlatBtn b in new[] { _salvar, _cancelar })
            {
                int bw = Largura(b);
                b.SetBounds(x, y, bw, Ui.S(32));
                x += bw + Ui.S(8);
            }
            int xr = w;
            foreach (FlatBtn b in new[] { _excluir, _depois, _antes })
            {
                if (!b.Visible) continue;
                int bw = Largura(b);
                xr -= bw;
                b.SetBounds(xr, y, bw, Ui.S(32));
                xr -= Ui.S(8);
            }
            // Pouca largura: os da direita descem para a linha de baixo.
            if (xr < x)
            {
                y += Ui.S(40);
                int xx = 0;
                foreach (FlatBtn b in new[] { _antes, _depois, _excluir })
                {
                    if (!b.Visible) continue;
                    b.SetBounds(xx, y, Largura(b), Ui.S(32));
                    xx += b.Width + Ui.S(8);
                }
            }
            return y + Ui.S(32);
        }

        private static void Linha(Label l, Control c, ref int y, int lw, int cx, int cw, int h)
        {
            l.SetBounds(0, y + (h - Ui.S(18)) / 2, lw, Ui.S(18));
            c.SetBounds(cx, y, cw, h);
            y += h + Ui.S(12);
        }

        private static int Largura(FlatBtn b) { return TextRenderer.MeasureText(b.Text, b.Font).Width + Ui.S(28); }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(Ui.Misturar(Ui.Surface, Ui.SurfaceAlt, 0.55f))) e.Graphics.FillRectangle(b, ClientRectangle);
        }
    }

    /// <summary>Timer de desligar: 30 min, 1 h, 2 h; com timer ativo, a contagem e o Cancelar.</summary>
    internal sealed class PainelDoTimer : Control
    {
        private readonly FlatBtn _t30, _t60, _t120, _cancelar, _cancelarRetorno;
        private readonly Label _contagem, _nota, _retorno;
        private readonly System.Windows.Forms.Timer _relogio;
        private Aparelho _a;

        public PainelDoTimer()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            _t30 = Botao("30 min", 30); _t60 = Botao("1 h", 60); _t120 = Botao("2 h", 120);
            _cancelar = new FlatBtn();
            _cancelar.Text = T.Cancel;
            _cancelar.Danger = true;
            _cancelar.Click += delegate { ServicoCasa.CancelarTimer(); };
            Controls.Add(_cancelar);
            _contagem = new Label();
            _contagem.Font = Ui.FontMed; _contagem.ForeColor = Ui.AccentText; _contagem.AutoSize = false; _contagem.BackColor = Color.Transparent;
            Controls.Add(_contagem);
            _retorno = new Label();
            _retorno.Font = Ui.FontMed; _retorno.ForeColor = Ui.AccentText; _retorno.AutoSize = false; _retorno.BackColor = Color.Transparent;
            Controls.Add(_retorno);
            _cancelarRetorno = new FlatBtn();
            _cancelarRetorno.Text = T.CasaCancelarRetorno;
            _cancelarRetorno.Click += delegate { ServicoCasa.CancelarRetorno(); };
            Controls.Add(_cancelarRetorno);
            _nota = new Label();
            _nota.Text = T.CasaTimerNota;
            _nota.Font = Ui.FontSmall; _nota.ForeColor = Ui.Muted; _nota.AutoSize = false; _nota.BackColor = Color.Transparent;
            Controls.Add(_nota);
            _relogio = new System.Windows.Forms.Timer();
            _relogio.Interval = 1000;
            _relogio.Tick += delegate { Contagem(); };
        }

        private FlatBtn Botao(string t, int min)
        {
            FlatBtn b = new FlatBtn();
            b.Text = t;
            b.Click += delegate { if (_a != null) ServicoCasa.DefinirTimer(_a.Puid, min); };
            Controls.Add(b);
            return b;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _relogio.Dispose();
            base.Dispose(disposing);
        }

        public bool Disponivel { get { return _a != null && CenaCasa.Aceita(_a, "t_power", 0); } }

        public void Mostrar(Aparelho a)
        {
            _a = a;
            bool on = a != null && a.Online;
            foreach (FlatBtn b in new[] { _t30, _t60, _t120 }) b.Enabled = on;
            Contagem();
        }

        internal bool Ativo
        {
            get { DateTime? em = ServicoCasa.DesligarEm; return em.HasValue && _a != null && ServicoCasa.DesligarPuid == _a.Puid; }
        }

        internal bool RetornoAtivo
        {
            get { DateTime? em = ServicoCasa.RetornoEm; return em.HasValue && _a != null && ServicoCasa.RetornoPuid == _a.Puid; }
        }

        private static string Falta(DateTime em)
        {
            TimeSpan falta = em - DateTime.Now;
            if (falta < TimeSpan.Zero) falta = TimeSpan.Zero;
            return falta.TotalHours >= 1 ? string.Format("{0} h {1:00} min", (int)falta.TotalHours, falta.Minutes)
                                         : string.Format("{0}:{1:00}", falta.Minutes, falta.Seconds);
        }

        private void Contagem()
        {
            bool retorno = RetornoAtivo;
            bool mudou = _retorno.Visible != retorno;
            _retorno.Visible = _cancelarRetorno.Visible = retorno;
            if (retorno) _retorno.Text = T.CasaRetornoAtivo(ServicoCasa.RetornoEm.Value.ToString("HH:mm"), Falta(ServicoCasa.RetornoEm.Value));
            if (mudou && Parent != null) { Control c = Parent; while (c != null && !(c is CartaoDeAparelho)) c = c.Parent; if (c != null) ((CartaoDeAparelho)c).PedirArranjo(); }
            bool ativo = Ativo;
            _cancelar.Visible = ativo;
            if (ativo)
            {
                DateTime em = ServicoCasa.DesligarEm.Value;
                TimeSpan falta = em - DateTime.Now;
                if (falta < TimeSpan.Zero) falta = TimeSpan.Zero;
                string f = falta.TotalHours >= 1 ? string.Format("{0} h {1:00} min", (int)falta.TotalHours, falta.Minutes)
                                                 : string.Format("{0}:{1:00}", falta.Minutes, falta.Seconds);
                _contagem.Text = T.CasaTimerAtivo(em.ToString("HH:mm"), f);
                if (!_relogio.Enabled) _relogio.Start();
            }
            else
            {
                _contagem.Text = T.CasaTimerInativo;
                if (!retorno) _relogio.Stop();
            }
            if (retorno && !_relogio.Enabled) _relogio.Start();
            _contagem.ForeColor = ativo ? Ui.AccentText : Ui.Muted;
        }

        public int Arranjar(int w)
        {
            int x = 0, h = Ui.S(32);
            foreach (FlatBtn b in new[] { _t30, _t60, _t120 })
            {
                int bw = Math.Max(Ui.S(64), TextRenderer.MeasureText(b.Text, b.Font).Width + Ui.S(28));
                b.SetBounds(x, 0, bw, h);
                x += bw + Ui.S(8);
            }
            int cw = TextRenderer.MeasureText(_cancelar.Text, _cancelar.Font).Width + Ui.S(28);
            int y = h + Ui.S(12);
            _contagem.SetBounds(0, y, Math.Max(Ui.S(40), w - (_cancelar.Visible ? cw + Ui.S(8) : 0)), Ui.S(24));
            _cancelar.SetBounds(w - cw, y - Ui.S(4), cw, h);
            y += Ui.S(30);
            if (_retorno.Visible)
            {
                int rw = TextRenderer.MeasureText(_cancelarRetorno.Text, _cancelarRetorno.Font).Width + Ui.S(28);
                _retorno.SetBounds(0, y + Ui.S(4), Math.Max(Ui.S(40), w - rw - Ui.S(8)), Ui.S(24));
                _cancelarRetorno.SetBounds(w - rw, y, rw, h);
                y += h + Ui.S(10);
            }
            Size s = TextRenderer.MeasureText(_nota.Text, _nota.Font, new Size(w, 0), TextFormatFlags.WordBreak);
            _nota.SetBounds(0, y, w, s.Height + Ui.S(2));
            return _nota.Bottom;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(Ui.Misturar(Ui.Surface, Ui.SurfaceAlt, 0.55f))) e.Graphics.FillRectangle(b, ClientRectangle);
        }
    }

    /// <summary>
    /// Botao do diagnostico no cabecalho do cartao: chave de boca desenhada,
    /// com um ponto vermelho quando o aparelho acusa falha.
    /// </summary>
    internal sealed class BotaoChave : Control
    {
        public bool Falha, Aberto;
        private Transicao _t;
        private Transicao TH { get { return _t ?? (_t = new Transicao(this)); } }

        public BotaoChave()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            AccessibleName = T.CasaDiagnostico;
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
            using (SolidBrush b = new SolidBrush(Ui.Surface)) g.FillRectangle(b, ClientRectangle);
            Ui.Smooth(g);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fundo = Aberto ? Ui.AccentSoft : Ui.Misturar(Ui.Surface, Ui.Hover, TH.Valor);
            using (SolidBrush b = new SolidBrush(fundo)) g.FillEllipse(b, r);
            using (Pen p = new Pen(Aberto ? Ui.Accent : Ui.Border)) g.DrawEllipse(p, r);
            if (Focused && ShowFocusCues) using (Pen p = new Pen(Ui.Accent, 2f)) g.DrawEllipse(p, Rectangle.Inflate(r, -2, -2));
            float m = Width * 0.27f;
            IconesCasa.ChaveDeBoca(g, new RectangleF(m, m, Width - 2 * m, Height - 2 * m), Aberto ? Ui.AccentText : Ui.Text);
            if (Falha)
            {
                int d = Ui.S(9);
                using (SolidBrush b = new SolidBrush(Ui.Surface)) g.FillEllipse(b, Width - d - Ui.S(1) - 2, Ui.S(1) - 2, d + 4, d + 4);
                using (SolidBrush b = new SolidBrush(Ui.Danger)) g.FillEllipse(b, Width - d - Ui.S(1), Ui.S(1), d, d);
            }
        }
    }

    internal static partial class IconesCasa
    {
        /// <summary>Chave de boca: cabo na diagonal e a cabeca aberta no alto.</summary>
        public static void ChaveDeBoca(Graphics g, RectangleF r, Color c)
        {
            float s = Math.Min(r.Width, r.Height);
            float esp = Math.Max(2f, s / 6.5f);
            using (Pen p = new Pen(c, esp))
            {
                p.StartCap = p.EndCap = LineCap.Round;
                // Cabo: do canto de baixo a esquerda ate perto da cabeca.
                g.DrawLine(p, r.X + s * 0.12f, r.Bottom - s * 0.12f, r.X + s * 0.55f, r.Y + s * 0.45f);
                // Cabeca: anel aberto para o canto de cima a direita (a boca).
                float d = s * 0.5f;
                RectangleF cab = new RectangleF(r.Right - d - s * 0.04f, r.Y + s * 0.04f, d, d);
                g.DrawArc(p, cab, 25f, 290f);
            }
        }

        /// <summary>"Manter": circulo com um traco.</summary>
        public static void Manter(Graphics g, RectangleF r, Color c)
        {
            using (Pen p = new Pen(c, Math.Max(1.5f, r.Width / 12f)))
            {
                g.DrawEllipse(p, r);
                g.DrawLine(p, r.X + r.Width * 0.3f, r.Y + r.Height / 2, r.Right - r.Width * 0.3f, r.Y + r.Height / 2);
            }
        }

        /// <summary>Simbolo de energia, para cenas de desligar.</summary>
        public static void Energia(Graphics g, RectangleF r, Color c)
        {
            using (Pen p = new Pen(c, Math.Max(1.5f, r.Width / 10f)))
            {
                p.StartCap = p.EndCap = LineCap.Round;
                g.DrawArc(p, r.X + r.Width * 0.1f, r.Y + r.Height * 0.15f, r.Width * 0.8f, r.Height * 0.8f, -60, 300);
                g.DrawLine(p, r.X + r.Width / 2, r.Y, r.X + r.Width / 2, r.Y + r.Height * 0.45f);
            }
        }
    }

    public static partial class T
    {
        public static string CasaNovaCena { get { return P("Nova cena", "New scene"); } }
        public static string CasaEditar { get { return P("Editar", "Edit"); } }
        public static string CasaConcluir { get { return P("Concluir", "Done"); } }
        public static string CasaRestaurarPadroes { get { return P("Restaurar padrões", "Restore defaults"); } }
        public static string CasaEditarCena { get { return P("Editar cena", "Edit scene"); } }
        public static string CasaCenaNome { get { return P("Nome", "Name"); } }
        public static string CasaCenaIcone { get { return P("Ícone", "Icon"); } }
        public static string CasaCenaAcao { get { return P("Ação", "Action"); } }
        public static string CasaCenaLigarAjustar { get { return P("Ligar e ajustar", "Turn on and set"); } }
        public static string CasaCenaSoDesliga { get { return P("Desligar", "Turn off"); } }
        public static string CasaCenaSoLiga { get { return P("Só liga", "Turn on only"); } }
        public static string CasaCenaAjustar { get { return P("Ajustar", "Set"); } }
        public static string CasaCenaIndisponivel { get { return P("indisponível neste modelo", "not available on this model"); } }
        public static string CasaManter { get { return P("Manter", "Keep"); } }
        public static string CasaLigar { get { return P("Ligar", "On"); } }
        public static string CasaDesligarCurto { get { return P("Desligar", "Off"); } }
        public static string CasaMoverAntes { get { return P("Antes", "Earlier"); } }
        public static string CasaMoverDepois { get { return P("Depois", "Later"); } }
        public static string CasaExcluir { get { return P("Excluir", "Delete"); } }
        public static string CasaNoturnoCurto { get { return P("noturno", "sleep"); } }
        public static string CasaSemNoturnoCurto { get { return P("sem noturno", "no sleep"); } }
        public static string CasaTimer { get { return P("Desligar automaticamente", "Auto off"); } }
        public static string CasaPreferencias { get { return P("Preferências", "Preferences"); } }
        public static string CasaTimerInativo { get { return P("Nenhum timer ativo.", "No timer set."); } }
        public static string CasaTimerAtivo(string hora, string falta) { return P("Desliga às " + hora + "  ·  em " + falta, "Turns off at " + hora + "  ·  in " + falta); }
        public static string CasaTimerNota { get { return P("Só funciona com o PC ligado e o Mhiagos rodando.", "Only works with the PC on and Mhiagos running."); } }
        public static string CasaRetornoPadrao { get { return P("Padrão", "Default"); } }
        public static string CasaRetornoTitulo(string nome) { return P(nome + ": de volta ao padrão", nome + ": back to default"); }
        public static string CasaRetornoFeito { get { return P("A cena terminou e o ar voltou ao padrão.", "The scene ended and the AC is back to default."); } }
        public static string CasaPorMinutos(int min) { return min >= 60 ? P("por " + (min / 60) + " h", "for " + (min / 60) + " h") : P("por " + min + " min", "for " + min + " min"); }
        public static string CasaVoltarDepois { get { return P("Voltar ao padrão depois de", "Back to default after"); } }
        public static string CasaNunca { get { return P("Nunca", "Never"); } }
        public static string CasaRetornoAtivo(string hora, string falta) { return P("Volta ao padrão às " + hora + "  ·  em " + falta, "Back to default at " + hora + "  ·  in " + falta); }
        public static string CasaCancelarRetorno { get { return P("Manter a cena", "Keep the scene"); } }
        public static string CasaRetornoSecao { get { return P("Padrão depois das cenas", "Default after scenes"); } }
        public static string CasaRetornoNota { get { return P("Para onde o ar volta quando uma cena com tempo termina. Mexer no ar na mão cancela a volta.", "Where the AC returns when a timed scene ends. Changing the AC by hand cancels the return."); } }
    }
}
