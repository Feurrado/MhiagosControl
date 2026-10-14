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
        private readonly List<string[]> _discos = new List<string[]>();   // tamanho, nome
        private readonly List<double> _discosGb = new List<double>();
        private bool _sobre;

        private const int Pad = 16;

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
                // a linha de cada pente: "8 GB · Kingston · KHX2666C16/8G"
                foreach (string[] l in _g.Linhas)
                    if (l[1].IndexOf("GB", StringComparison.Ordinal) > 0 && l[1].IndexOf('·') > 0)
                    { _sub = l[1].Replace("  ", " "); break; }
            }
            else if (t == T.SpecGpu)
            {
                _destaque = SystemInfo.Limpar(V(T.SpecModel)) ?? V(T.SpecModel);
                _sub = V(T.SpecMonitor);
                Etiqueta(V(T.SpecVram));
                Etiqueta(V(T.SpecResolution) == null ? null : V(T.SpecResolution).Replace("  ·  ", " · "));
                if (V(T.SpecDriver) != null) Etiqueta(T.SpecDriverTag(V(T.SpecDriver)));
            }
            else if (t == T.SpecStorage)
            {
                foreach (string[] l in _g.Linhas)
                {
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
            int h = Pad + 16 + 4;                         // titulo
            h += 26;                                      // destaque
            if (!string.IsNullOrEmpty(_sub)) h += 18;
            if (_etiquetas.Count > 0) h += 8 + LinhasDeEtiquetas(largura) * 26;
            if (_slotsTotal > 0) h += 10 + 34;
            if (_discos.Count > 0) h += 6 + _discos.Count * 34;
            return h + Pad - 4;
        }

        private int LinhasDeEtiquetas(int largura)
        {
            int x = Pad, linhas = 1;
            foreach (string e in _etiquetas)
            {
                int w = TextRenderer.MeasureText(e, Ui.FontSmall).Width + 14;
                if (x > Pad && x + w > largura - Pad) { linhas++; x = Pad; }
                x += w + 6;
            }
            return linhas;
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _sobre = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _sobre = false; Invalidate(); }

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

            int y = Pad - 2;
            TextRenderer.DrawText(g, _g.Titulo, Ui.FontSmall, new Point(Pad, y), Ui.Muted, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, T.SpecSeeAll, Ui.FontSmall, new Rectangle(Pad, y, Width - 2 * Pad, 16),
                _sobre ? Ui.Accent : Ui.Faint, TextFormatFlags.Right | TextFormatFlags.NoPadding);
            y += 20;

            TextRenderer.DrawText(g, _destaque ?? "", Ui.FontTileValue, new Rectangle(Pad, y, Width - 2 * Pad, 24),
                Ui.Text, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            y += 26;
            if (!string.IsNullOrEmpty(_sub))
            {
                TextRenderer.DrawText(g, _sub, Ui.FontSmall, new Rectangle(Pad, y, Width - 2 * Pad, 16),
                    Ui.Muted, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                y += 18;
            }

            if (_etiquetas.Count > 0)
            {
                y += 8;
                int x = Pad;
                foreach (string et in _etiquetas)
                {
                    int w = TextRenderer.MeasureText(et, Ui.FontSmall).Width + 14;
                    if (x > Pad && x + w > Width - Pad) { x = Pad; y += 26; }
                    Rectangle c = new Rectangle(x, y, Math.Min(w, Width - 2 * Pad), 20);
                    using (GraphicsPath p = Ui.RoundRect(c, 10))
                    using (SolidBrush b = new SolidBrush(Ui.SurfaceAlt)) g.FillPath(b, p);
                    using (GraphicsPath p = Ui.RoundRect(c, 10))
                    using (Pen pen = new Pen(Ui.Border)) g.DrawPath(pen, p);
                    TextRenderer.DrawText(g, et, Ui.FontSmall, c, Ui.Text,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                    x += w + 6;
                }
                y += 26;
            }

            if (_slotsTotal > 0)
            {
                y += 10;
                for (int i = 0; i < _slotsTotal; i++)
                {
                    Rectangle s = new Rectangle(Pad + i * 22, y, 14, 30);
                    bool cheio = i < _slotsUsados;
                    using (GraphicsPath p = Ui.RoundRect(s, 2))
                    {
                        using (SolidBrush b = new SolidBrush(cheio ? Ui.Accent : Ui.SurfaceAlt)) g.FillPath(b, p);
                        using (Pen pen = new Pen(cheio ? Ui.Accent : Ui.Border)) g.DrawPath(pen, p);
                    }
                }
                TextRenderer.DrawText(g, T.SpecSlotsUsed(_slotsUsados, _slotsTotal), Ui.FontSmall,
                    new Point(Pad + _slotsTotal * 22 + 8, y + 8), Ui.Muted, TextFormatFlags.NoPadding);
                y += 34;
            }

            if (_discos.Count > 0)
            {
                y += 6;
                double maior = 1;
                foreach (double v in _discosGb) if (v > maior) maior = v;
                for (int i = 0; i < _discos.Count; i++)
                {
                    int w = Width - 2 * Pad;
                    TextRenderer.DrawText(g, _discos[i][1], Ui.FontSemi, new Rectangle(Pad, y, w - 70, 16),
                        Ui.Text, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                    TextRenderer.DrawText(g, _discos[i][0], Ui.FontSmall, new Rectangle(Pad, y, w, 16),
                        Ui.Muted, TextFormatFlags.Right | TextFormatFlags.NoPadding);
                    if (_discosGb[i] <= 0) { y += 26; continue; }   // sem tamanho (leitor USB): sem barra
                    Rectangle trilho = new Rectangle(Pad, y + 19, w, 6);
                    using (GraphicsPath p = Ui.RoundRect(trilho, 3))
                    using (SolidBrush b = new SolidBrush(Ui.SurfaceAlt)) g.FillPath(b, p);
                    int cheio = Math.Max(6, (int)(w * Math.Min(1.0, _discosGb[i] / maior)));
                    using (GraphicsPath p = Ui.RoundRect(new Rectangle(Pad, y + 19, cheio, 6), 3))
                    using (SolidBrush b = new SolidBrush(Ui.Accent)) g.FillPath(b, p);
                    y += 34;
                }
            }
        }
    }
}
