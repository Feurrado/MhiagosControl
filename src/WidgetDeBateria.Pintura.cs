using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace MhiagosControl
{
    public sealed partial class WidgetDeBateria : Form
    {
        internal void Renderizar()
        {
            System.Diagnostics.Stopwatch medir = QuadrosEm != null ? System.Diagnostics.Stopwatch.StartNew() : null;
            try { RenderizarAgora(); }
            finally { if (medir != null) { QuadrosEm.Add(Motion.Agora); QuadrosMs.Add(medir.Elapsed.TotalMilliseconds); } }
        }

        private void RenderizarAgora()
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                Size tam = Medida();
                if (tam != Size)
                {
                    Rectangle antes = Bounds;
                    // cresce para o lado da tela que tem mais espaco: perto da
                    // borda direita (ou de baixo) a borda do widget fica parada
                    Rectangle area = Screen.FromRectangle(antes).WorkingArea;
                    Point onde = AncorarCrescimento(antes, tam, area);
                    onde = new Point(Math.Max(area.Left, Math.Min(onde.X, area.Right - tam.Width)),
                                     Math.Max(area.Top, Math.Min(onde.Y, area.Bottom - tam.Height)));
                    // posicao e tamanho numa chamada so: em duas, o quadro do
                    // meio mostra o widget no lugar errado e os icones pulam
                    Bounds = new Rectangle(onde, tam);
                    AplicarRegiao(tam);
                }

                PintorDoWidget.Quadro q = new PintorDoWidget.Quadro();
                q.Itens = _itens;
                q.Notas = _notas;
                q.Presenca = _presenca;
                q.Layout = _cfg.Layout;
                q.Escuro = Escuro;
                q.Fosco = _cfg.Fosco && !_cfg.Nitido;
                q.Nitido = _cfg.Nitido;
                q.Sobre = _sobre;
                q.Escala = _escala;
                q.Casa = _casa;
                q.Fase = Ui.Animacoes ? (Environment.TickCount & int.MaxValue) % CicloMs / (float)CicloMs : 0f;
                bool transicao = false;
                q.Carga = new float[_itens.Length];
                for (int i = 0; i < _itens.Length; i++)
                {
                    bool animando;
                    q.Carga[i] = TransicaoDeCarga.Valor(HistoricoDeBateria.Chave(_itens[i]),
                        _itens[i].Carregando && _itens[i].Bateria.HasValue, out animando);
                    transicao |= animando;
                }
                _transicao = transicao;

                // O mesmo bitmap enquanto o tamanho nao muda: carregando, o
                // widget pinta 25 quadros por segundo, e alocar uma imagem
                // ARGB inteira em cada um so dava trabalho ao coletor.
                // A DIB do quadro e criada uma vez por tamanho, ja num DC de memoria: o GDI+
                // pinta direto na memoria que o UpdateLayeredWindow le, sem GetHbitmap
                // (copia e conversao do bitmap inteiro) nem DC novo a cada quadro.
                GarantirQuadro(tam);
                using (Graphics g = Graphics.FromImage(_quadro))
                {
                    g.Clear(Color.Transparent);
                    PintorDoWidget.Pintar(g, tam, q);
                }
                // o ar da casa animando (ligar, desligar, alvo) tambem pede quadros
                if (_casa.Animando) _transicao = true;
                AplicarQuadro(tam);
                // o ultimo quadro da transicao desliga o relogio se nada mais carrega
                AjustarAnimacao();
            }
            catch (Exception ex)
            {
                // um quadro perdido nao pode virar uma enxurrada no log a 15 por segundo
                _animacao.Stop();
                Log.Error("desenho do widget", ex);
            }
        }

        private IntPtr _dib, _dcDoQuadro, _antesDoDib;

        /// <summary>Testes: quantas vezes a DIB do quadro foi (re)criada.</summary>
        internal int QuadrosCriados;

        private void GarantirQuadro(Size tam)
        {
            if (_quadro != null && _quadro.Size == tam && _dib != IntPtr.Zero) return;
            LiberarQuadro();
            BITMAPINFOHEADER bi = new BITMAPINFOHEADER();
            bi.biSize = Marshal.SizeOf(typeof(BITMAPINFOHEADER));
            bi.biWidth = tam.Width;
            bi.biHeight = -tam.Height;   // de cima para baixo, como o Bitmap do GDI+
            bi.biPlanes = 1;
            bi.biBitCount = 32;
            IntPtr tela = GetDC(IntPtr.Zero);
            try
            {
                IntPtr bits;
                _dib = CreateDIBSection(tela, ref bi, 0, out bits, IntPtr.Zero, 0);
                if (_dib == IntPtr.Zero || bits == IntPtr.Zero) throw new InvalidOperationException("CreateDIBSection falhou");
                _dcDoQuadro = CreateCompatibleDC(tela);
                _antesDoDib = SelectObject(_dcDoQuadro, _dib);
                // PArgb: o UpdateLayeredWindow quer alfa pre-multiplicado, que e como o GDI+ guarda esse formato
                _quadro = new Bitmap(tam.Width, tam.Height, tam.Width * 4, PixelFormat.Format32bppPArgb, bits);
                QuadrosCriados++;
            }
            finally { ReleaseDC(IntPtr.Zero, tela); }
        }

        private void AplicarQuadro(Size tam)
        {
            IntPtr tela = GetDC(IntPtr.Zero);
            try
            {
                SIZE tamanho = new SIZE(tam.Width, tam.Height);
                POINT origem = new POINT(0, 0);
                POINT topo = new POINT(Left, Top);
                BLENDFUNCTION b = new BLENDFUNCTION();
                b.BlendOp = AC_SRC_OVER;
                b.SourceConstantAlpha = 255;
                b.AlphaFormat = AC_SRC_ALPHA;
                UpdateLayeredWindow(Handle, tela, ref topo, ref tamanho, _dcDoQuadro, ref origem, 0, ref b, ULW_ALPHA);
            }
            finally { ReleaseDC(IntPtr.Zero, tela); }
        }

        private void LiberarQuadro()
        {
            if (_quadro != null) { _quadro.Dispose(); _quadro = null; }
            if (_dcDoQuadro != IntPtr.Zero) { SelectObject(_dcDoQuadro, _antesDoDib); DeleteDC(_dcDoQuadro); _dcDoQuadro = IntPtr.Zero; }
            if (_dib != IntPtr.Zero) { DeleteObject(_dib); _dib = IntPtr.Zero; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public int biSize, biWidth, biHeight;
            public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        }

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr secao, uint deslocamento);

        private void AplicarBitmap(Bitmap bmp) { AplicarBitmap(this, bmp); }

        /// <summary>Entrega o quadro a uma janela em camadas (tambem o widget da casa).</summary>
        internal static void AplicarBitmap(Form janela, Bitmap bmp)
        {
            IntPtr tela = GetDC(IntPtr.Zero);
            IntPtr mem = CreateCompatibleDC(tela);
            IntPtr hbmp = IntPtr.Zero, antigo = IntPtr.Zero;
            try
            {
                hbmp = bmp.GetHbitmap(Color.FromArgb(0));
                antigo = SelectObject(mem, hbmp);
                SIZE tamanho = new SIZE(bmp.Width, bmp.Height);
                POINT origem = new POINT(0, 0);
                POINT topo = new POINT(janela.Left, janela.Top);
                BLENDFUNCTION b = new BLENDFUNCTION();
                b.BlendOp = AC_SRC_OVER;
                b.SourceConstantAlpha = 255;
                b.AlphaFormat = AC_SRC_ALPHA;
                UpdateLayeredWindow(janela.Handle, tela, ref topo, ref tamanho, mem, ref origem, 0, ref b, ULW_ALPHA);
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, tela);
                if (hbmp != IntPtr.Zero) { SelectObject(mem, antigo); DeleteObject(hbmp); }
                DeleteDC(mem);
            }
        }

        /// <summary>
        /// Recorta a janela no formato da placa, para o clique.
        /// </summary>
        private void AplicarRegiao(Size tam) { AplicarRegiao(Handle, tam, _escala); }

        internal static void AplicarRegiao(IntPtr janela, Size tam, float escala)
        {
            int d = (int)Math.Round(PintorDoWidget.RaioDoVidro * 2f * escala);
            try
            {
                IntPtr rgn = CreateRoundRectRgn(0, 0, tam.Width + 1, tam.Height + 1, d, d);
                // a regiao passa a ser do sistema: nao se apaga depois de entregar
                if (SetWindowRgn(janela, rgn, true) == 0) DeleteObject(rgn);
            }
            catch (Exception ex) { Log.Error("regiao do widget", ex); }
        }

        /// <summary>
        /// O desfoque do Windows ignora a regiao da janela e saia quadrado fora
        /// da curva dos cantos (SetWindowRgn e DwmEnableBlurBehindWindow nao
        /// recortam, nem numa janela comum; visto no Windows 11). So o
        /// arredondamento do proprio DWM corta o desfoque, e o raio dele e fixo
        /// em 8 px na escala 1: por isso a placa e desenhada com esse raio. No
        /// Windows 10 a chamada falha calada e o desfoque fica retangular.
        /// </summary>
        private void ArredondarCantos() { ArredondarCantos(Handle); }

        internal static void ArredondarCantos(IntPtr janela)
        {
            if (!Windows11) return;
            try
            {
                int redondo = DWMWCP_ROUND;
                DwmSetWindowAttribute(janela, DWMWA_WINDOW_CORNER_PREFERENCE, ref redondo, 4);
            }
            catch (Exception ex) { Log.Error("cantos do widget", ex); }
        }

        /// <summary>
        /// Desfoque do que esta atras. O fosco usa o acrilico do Windows 11, de
        /// desfoque mais forte; no Windows 10 o acrilico atrasa ao arrastar, e
        /// os dois ficam no desfoque simples.
        /// </summary>
        private void AplicarDesfoque() { AplicarDesfoque(Handle, _cfg); }

        internal static void AplicarDesfoque(IntPtr janela, ConfigDoWidget cfg)
        {
            try
            {
                AccentPolicy ap = new AccentPolicy();
                bool acrilico = cfg.Fosco && !cfg.Nitido && Windows11;
                ap.AccentState = cfg.Nitido ? ACCENT_DISABLED
                               : acrilico ? ACCENT_ENABLE_ACRYLICBLURBEHIND : ACCENT_ENABLE_BLURBEHIND;
                // tinta quase nula: com alfa zero o acrilico some em algumas versoes
                ap.GradientColor = acrilico ? 0x01000000u : 0u;
                int tamanho = Marshal.SizeOf(ap);
                IntPtr p = Marshal.AllocHGlobal(tamanho);
                try
                {
                    Marshal.StructureToPtr(ap, p, false);
                    WindowCompositionAttributeData d = new WindowCompositionAttributeData();
                    d.Attribute = WCA_ACCENT_POLICY;
                    d.SizeOfData = tamanho;
                    d.Data = p;
                    SetWindowCompositionAttribute(janela, ref d);
                }
                finally { Marshal.FreeHGlobal(p); }
            }
            catch (Exception ex) { Log.Error("desfoque do widget", ex); }
        }

        /// <summary>
        /// Pelo RtlGetVersion, e nao pelo Environment.OSVersion: sem manifesto
        /// de compatibilidade (a previa, por exemplo) o .NET responde 6.2 num
        /// Windows 11.
        /// </summary>
        private static bool Windows11
        {
            get
            {
                Version v = Environment.OSVersion.Version;
                try
                {
                    OSVERSIONINFOEX info = new OSVERSIONINFOEX();
                    info.dwOSVersionInfoSize = Marshal.SizeOf(typeof(OSVERSIONINFOEX));
                    if (RtlGetVersion(ref info) == 0)
                        v = new Version(info.dwMajorVersion, info.dwMinorVersion, info.dwBuildNumber);
                }
                catch (Exception ex) { Log.Error("versao do Windows", ex); }
                return v.Major > 10 || (v.Major == 10 && v.Build >= 22000);
            }
        }
    }
}
