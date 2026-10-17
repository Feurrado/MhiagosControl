using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// Um bloco do resumo da pagina Especificacoes: a peca em destaque, os
    /// numeros que importam em etiquetas e, quando ajuda, um desenho - os slots
    /// de memoria ocupados e o tamanho de cada disco em barras.
    ///
    /// O resumo anterior eram cartoes com as tres primeiras linhas da tabela,
    /// e as tres primeiras nem sempre sao as que se procura: no processador
    /// apareciam modelo, fabricante e "AMD64 Family 25 Model 33", e os nucleos
    /// ficavam escondidos. Aqui cada peca escolhe o que mostrar. A tabela
    /// inteira continua a um clique, no proprio bloco.
    /// </summary>
    public class BlocoDeSpec : Control
    {
        private readonly SpecGrupo _g;
        private string _destaque, _sub;
        private readonly List<string> _etiquetas = new List<string>();
        private int _slotsUsados, _slotsTotal;
        private List<SlotDeMemoria> _slots;
        private readonly List<Rectangle> _areasDosSlots = new List<Rectangle>();
        private readonly ToolTip _dica = new ToolTip();
        private int _slotSobMouse = -1;
        private readonly List<string[]> _discos = new List<string[]>();   // tamanho, nome
        private readonly List<double> _discosGb = new List<double>();
        private readonly List<double> _discosUsados = new List<double>();   // GB ocupados, -1 se desconhecido
        private bool _sobre;

        private static readonly int Pad = Ui.S(16);

        public BlocoDeSpec(SpecGrupo g)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Ui.Window;
            Font = Ui.FontBase;
            Cursor = Cursors.Hand;
            _g = g;
            Montar();
        }

        public SpecGrupo Grupo { get { return _g; } }

        /// <summary>O bloco que vale a largura inteira: discos lado a lado e o processador.</summary>
        public bool Largo
        {
            get { return _g.Titulo == T.SpecStorage || _g.Titulo == T.SpecCpu; }
        }

        private string V(string rotulo)
        {
            foreach (string[] l in _g.Linhas) if (l[0] == rotulo) return l[1];
            return null;
        }

        private void Etiqueta(string texto)
        {
            if (!string.IsNullOrEmpty(texto)) _etiquetas.Add(texto.Trim());
        }

        private void Montar()
        {
            string t = _g.Titulo;
            if (t == T.SpecCpu)
            {
                _destaque = SystemInfo.Limpar(V(T.SpecModel)) ?? V(T.SpecModel);
                _sub = V(T.SpecVendor);
                string n = V(T.SpecCores), th = V(T.SpecThreads), clk = V(T.SpecMaxClock);
                if (n != null) Etiqueta(T.SpecCoresTag(n));
                if (th != null) Etiqueta(T.SpecThreadsTag(th));
                if (clk != null) Etiqueta(EmGhz(clk));
                if (V(T.SpecL3) != null) Etiqueta("L3 " + V(T.SpecL3));
                Etiqueta(V(T.SpecSocket));
            }
            else if (t == T.SpecBoard)
            {
                _destaque = V(T.SpecModel);
                _sub = V(T.SpecVendor);
                if (V(T.SpecBiosVersion) != null) Etiqueta("BIOS " + V(T.SpecBiosVersion));
                Etiqueta(V(T.SpecBiosDate));
            }
            else if (t == T.SpecRam)
            {
                _destaque = Junta(V(T.SpecTotal), V(T.SpecType));
                Etiqueta(V(T.SpecMemSpeed));
                if (V(T.SpecChannels) != null) Etiqueta(T.SpecChannelsTag(V(T.SpecChannels)));
                if (V(T.SpecMaxRam) != null) Etiqueta(T.SpecUpTo(V(T.SpecMaxRam)));
                Match m = Regex.Match(V(T.SpecSlots) ?? "", @"(\d+)\D+(\d+)");
                if (m.Success)
                {
                    _slotsUsados = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                    _slotsTotal = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                    if (_slotsTotal > 8 || _slotsUsados > _slotsTotal) _slotsTotal = 0;
                }
                // Com a tabela da BIOS, cada quadrado e um slot de verdade, no
                // lugar e com o nome dele.
                if (_g.Slots != null && _g.Slots.Count > 0 && _g.Slots.Count <= 8)
                {
                    _slots = _g.Slots;
                    _slotsTotal = _slots.Count;
                    _slotsUsados = 0;
                    foreach (SlotDeMemoria s in _slots) if (s.Ocupado) _slotsUsados++;
                }
                // a linha de cada pente: "8 GB · Kingston · KHX2666C16/8G"
                foreach (string[] l in _g.Linhas)
                    if (l[1].IndexOf("GB", StringComparison.Ordinal) > 0 && l[1].IndexOf('·') > 0)
                    { _sub = l[1].Replace("  ", " "); break; }
            }
            else if (t == T.SpecGpu)
            {
                // Com a marca da placa conhecida, o destaque e o nome de loja,
                // memoria inclusa, e a etiqueta da memoria sairia repetida.
                _destaque = _g.Destaque ?? SystemInfo.Limpar(V(T.SpecModel)) ?? V(T.SpecModel);
                _sub = V(T.SpecMonitor);
                if (_g.Destaque == null) Etiqueta(V(T.SpecVram));
                Etiqueta(V(T.SpecResolution) == null ? null : V(T.SpecResolution).Replace("  ·  ", " · "));
                if (V(T.SpecDriver) != null) Etiqueta(T.SpecDriverTag(V(T.SpecDriver)));
            }
            else if (t == T.SpecStorage)
            {
                int ix = 0;
                foreach (string[] l in _g.Linhas)
                {
                    _discosUsados.Add(ix < _g.Usados.Count ? _g.Usados[ix] : -1); ix++;
                    _discos.Add(new string[] { l[0], l[1].Replace("  ·  ", " · ") });
                    _discosGb.Add(Gigabytes(l[0]));
                }
                _destaque = T.SpecDisks(_discos.Count);
            }
            else if (t == T.SpecNetwork)
            {
                _destaque = V(T.SpecModel);
                Etiqueta(V(T.SpecLinkSpeed));
            }
            else if (t == T.SpecOs)
            {
                _destaque = V(T.SpecVersion) == null ? null : V(T.SpecVersion).Replace("  ·  ", " · ");
                Etiqueta(V(T.SpecArch));
                if (V(T.SpecUptime) != null) Etiqueta(T.SpecUptimeTag(V(T.SpecUptime)));
                if (V(T.SpecSecureBoot) != null) Etiqueta(T.SpecSecureBoot + " " + V(T.SpecSecureBoot));
                if (V(T.SpecTpm) != null) Etiqueta("TPM " + V(T.SpecTpm).Replace("  ·  ", " "));
            }

            // Grupo que ninguem previu: a primeira linha em destaque e as
            // seguintes em etiquetas, para nao sumir do resumo.
            if (string.IsNullOrEmpty(_destaque) && _g.Linhas.Count > 0)
            {
                _destaque = _g.Linhas[0][1];
                for (int i = 1; i < _g.Linhas.Count && i < 4; i++) Etiqueta(_g.Linhas[i][1]);
            }
        }

        private static string Junta(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b;
            if (string.IsNullOrEmpty(b)) return a;
            return a + " " + b;
        }

        private static string EmGhz(string mhz)
        {
            Match m = Regex.Match(mhz, @"\d+");
            int v;
            if (!m.Success || !int.TryParse(m.Value, out v) || v <= 0) return mhz;
            return (v / 1000.0).ToString("0.0", CultureInfo.CurrentCulture) + " GHz";
        }

        /// <summary>"112 GB", "1,8 TB" ou "932 GB" em gigabytes, para a barra.</summary>
        internal static double Gigabytes(string s)
        {
            Match m = Regex.Match(s ?? "", @"([\d.,]+)\s*(GB|TB)", RegexOptions.IgnoreCase);
            if (!m.Success) return 0;
            double v;
            string n = m.Groups[1].Value.Replace(',', '.');
            if (!double.TryParse(n, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return 0;
            return m.Groups[2].Value.ToUpperInvariant() == "TB" ? v * 1024 : v;
        }

        // ---------------- medida e desenho ----------------

        /// <summary>A altura que o conteudo pede na largura dada.</summary>
        public int AlturaPara(int largura)
        {
            int h = Pad + Ui.S(16) + Ui.S(4);                         // titulo
            h += Ui.S(26);                                      // destaque
            if (!string.IsNullOrEmpty(_sub)) h += Ui.S(18);
            if (_etiquetas.Count > 0) h += Ui.S(8) + LinhasDeEtiquetas(largura) * Ui.S(26);
            if (_slotsTotal > 0) h += Ui.S(10) + Ui.S(34) + (_slots != null ? AlturaDoNomeDoSlot : 0);
            if (_discos.Count > 0) h += Ui.S(6) + _discos.Count * Ui.S(34);
            return h + Pad - Ui.S(4);
        }

        private int LinhasDeEtiquetas(int largura)
        {
            int x = Pad, linhas = 1;
            foreach (string e in _etiquetas)
            {
                int w = TextRenderer.MeasureText(e, Ui.FontSmall).Width + Ui.S(14);
                if (x > Pad && x + w > largura - Pad) { linhas++; x = Pad; }
                x += w + Ui.S(6);
            }
            return linhas;
        }

        private static readonly int AlturaDoNomeDoSlot = Ui.S(16);

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _sobre = true; Invalidate(); }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _sobre = false;
            if (_slotSobMouse >= 0) { _slotSobMouse = -1; _dica.Hide(this); }
            Invalidate();
        }

        /// <summary>Sobre um slot, o pente dele: tamanho, marca e numero de peca.</summary>
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_slots == null) return;
            int i = -1;
            for (int k = 0; k < _areasDosSlots.Count; k++)
                if (_areasDosSlots[k].Contains(e.Location)) { i = k; break; }
            if (i == _slotSobMouse) return;
            _slotSobMouse = i;
            Invalidate();
            if (i < 0 || i >= _slots.Count) { _dica.Hide(this); return; }
            Rectangle a = _areasDosSlots[i];
            _dica.Show(TextoDoSlot(_slots[i]), this, a.Right + Ui.S(4), a.Y, 6000);
        }

        internal static string TextoDoSlot(SlotDeMemoria s)
        {
            if (!s.Ocupado) return T.SpecSlotEmpty(s.Nome);
            string t = s.Nome + ": " + SpecSheet.EmGigabytes(s.Bytes);
            if (!string.IsNullOrEmpty(s.Fabricante)) t += "  ·  " + s.Fabricante;
            if (!string.IsNullOrEmpty(s.Peca)) t += "  ·  " + s.Peca;
            return t;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _dica.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(Ui.Window)) g.FillRectangle(b, ClientRectangle);
            Ui.Smooth(g);

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Ui.RoundRect(r, Ui.Radius))
            {
                using (SolidBrush b = new SolidBrush(Ui.Surface)) g.FillPath(b, p);
                using (Pen pen = new Pen(_sobre ? Ui.Accent : Ui.Border)) g.DrawPath(pen, p);
            }

            int y = Pad - Ui.S(2);
            TextRenderer.DrawText(g, _g.Titulo, Ui.FontSmall, new Point(Pad, y), Ui.Muted, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, T.SpecSeeAll, Ui.FontSmall, new Rectangle(Pad, y, Width - 2 * Pad, Ui.S(16)),
                _sobre ? Ui.AccentText : Ui.Faint, TextFormatFlags.Right | TextFormatFlags.NoPadding);
            y += Ui.S(20);

            TextRenderer.DrawText(g, _destaque ?? "", Ui.FontTileValue, new Rectangle(Pad, y, Width - 2 * Pad, Ui.S(24)),
                Ui.Text, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            y += Ui.S(26);
            if (!string.IsNullOrEmpty(_sub))
            {
                TextRenderer.DrawText(g, _sub, Ui.FontSmall, new Rectangle(Pad, y, Width - 2 * Pad, Ui.S(16)),
                    Ui.Muted, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                y += Ui.S(18);
            }

            if (_etiquetas.Count > 0)
            {
                y += Ui.S(8);
                int x = Pad;
                foreach (string et in _etiquetas)
                {
                    int w = TextRenderer.MeasureText(et, Ui.FontSmall).Width + Ui.S(14);
                    if (x > Pad && x + w > Width - Pad) { x = Pad; y += Ui.S(26); }
                    Rectangle c = new Rectangle(x, y, Math.Min(w, Width - 2 * Pad), Ui.S(20));
                    using (GraphicsPath p = Ui.RoundRect(c, Ui.RaioCartao))
                    using (SolidBrush b = new SolidBrush(Ui.SurfaceAlt)) g.FillPath(b, p);
                    using (GraphicsPath p = Ui.RoundRect(c, Ui.RaioCartao))
                    using (Pen pen = new Pen(Ui.Border)) g.DrawPath(pen, p);
                    TextRenderer.DrawText(g, et, Ui.FontSmall, c, Ui.Text,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                    x += w + Ui.S(6);
                }
                y += Ui.S(26);
            }

            if (_slotsTotal > 0)
            {
                y += Ui.S(10);
                // Com os slots da BIOS, um pouco mais de passo: o nome embaixo
                // ("A1") e mais largo que o quadrado.
                bool nomeados = _slots != null;
                int passo = nomeados ? Ui.S(28) : Ui.S(22), larg = nomeados ? Ui.S(16) : Ui.S(14);
                _areasDosSlots.Clear();
                for (int i = 0; i < _slotsTotal; i++)
                {
                    Rectangle s = new Rectangle(Pad + i * passo, y, larg, Ui.S(30));
                    bool cheio = nomeados ? _slots[i].Ocupado : i < _slotsUsados;
                    bool sob = nomeados && i == _slotSobMouse;
                    using (GraphicsPath p = Ui.RoundRect(s, Ui.S(2)))
                    {
                        using (SolidBrush b = new SolidBrush(cheio ? Ui.Accent : Ui.SurfaceAlt)) g.FillPath(b, p);
                        using (Pen pen = new Pen(sob ? Ui.Text : (cheio ? Ui.Accent : Ui.Border))) g.DrawPath(pen, p);
                    }
                    if (!nomeados) continue;
                    Rectangle nome = new Rectangle(s.X - Ui.S(6), y + Ui.S(32), larg + Ui.S(12), AlturaDoNomeDoSlot - Ui.S(2));
                    TextRenderer.DrawText(g, _slots[i].Nome, Ui.FontSmall, nome, cheio ? Ui.Text : Ui.Faint,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
                    _areasDosSlots.Add(new Rectangle(s.X - Ui.S(5), y - Ui.S(2), larg + Ui.S(10), Ui.S(30) + AlturaDoNomeDoSlot + Ui.S(2)));
                }
                TextRenderer.DrawText(g, T.SpecSlotsUsed(_slotsUsados, _slotsTotal), Ui.FontSmall,
                    new Point(Pad + (_slotsTotal - 1) * passo + larg + Ui.S(8), y + Ui.S(8)), Ui.Muted, TextFormatFlags.NoPadding);
                y += Ui.S(34) + (nomeados ? AlturaDoNomeDoSlot : 0);
            }

            if (_discos.Count > 0)
            {
                y += Ui.S(6);
                for (int i = 0; i < _discos.Count; i++)
                {
                    int w = Width - 2 * Pad;
                    TextRenderer.DrawText(g, _discos[i][1], Ui.FontSemi, new Rectangle(Pad, y, w - Ui.S(150), Ui.S(16)),
                        Ui.Text, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                    bool temUso = _discosGb[i] > 0 && _discosUsados[i] >= 0;
                    string direita = temUso ? SpecSheet.EmGigabytes((ulong)(_discosUsados[i] * 1073741824.0)) + " / " + _discos[i][0] : _discos[i][0];
                    TextRenderer.DrawText(g, direita, Ui.FontSmall, new Rectangle(Pad, y, w, Ui.S(16)),
                        Ui.Muted, TextFormatFlags.Right | TextFormatFlags.NoPadding);
                    if (!temUso) { y += Ui.S(26); continue; }   // sem midia ou sem volume legivel: sem barra
                    Rectangle trilho = new Rectangle(Pad, y + Ui.S(19), w, Ui.S(6));
                    using (GraphicsPath p = Ui.RoundRect(trilho, Ui.S(3)))
                    using (SolidBrush b = new SolidBrush(Ui.SurfaceAlt)) g.FillPath(b, p);
                    int cheio = Math.Max(Ui.S(6), (int)(w * Math.Min(1.0, _discosUsados[i] / _discosGb[i])));
                    using (GraphicsPath p = Ui.RoundRect(new Rectangle(Pad, y + Ui.S(19), cheio, Ui.RaioControle), Ui.S(3)))
                    using (SolidBrush b = new SolidBrush(Ui.Accent)) g.FillPath(b, p);
                    y += Ui.S(34);
                }
            }
        }
    }
}
