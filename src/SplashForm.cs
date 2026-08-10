using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// Tela de carregamento do arranque.
    ///
    /// Abrir as fontes de sensores nao e instantaneo: as duas registram e sobem
    /// um driver, e percorrer o hardware pela primeira vez custa de centenas de
    /// milissegundos a alguns segundos.
    ///
    /// NAO aparece sozinha. Quem a chama e o clique no icone da bandeja durante
    /// esse intervalo - so quem estranhou a demora quer explicacao. Fecha-la nao
    /// interrompe nada: ela apenas observa a inicializacao, que roda noutra
    /// thread.
    ///
    /// A barra e indeterminada de proposito: nao ha como saber o progresso de
    /// uma enumeracao de hardware, e uma barra que finge porcentagem mente.
    /// </summary>
    /// <summary>
    /// A tela de carregamento vive numa thread propria, com laco de mensagens
    /// proprio.
    ///
    /// Mostra-la a partir da thread principal nao bastava: durante a subida do
    /// driver de sensores essa thread fica presa - LoadLibrary segura o cadeado
    /// do carregador e qualquer coisa que a interface precise carregar espera
    /// junto. Com a janela pendurada nela, o Windows a marcava como travada,
    /// trocava o cursor pelo de ocupado e engolia os cliques: nao dava para
    /// fechar.
    ///
    /// Numa thread so dela, a janela continua respondendo qualquer que seja o
    /// estado da principal. Fecha-la nao toca em nada: quem inicializa nem sabe
    /// que ela existe.
    /// </summary>
    public static class Splash
    {
        private static readonly object _lock = new object();
        private static SplashForm _form;
        private static Thread _thread;
        private static string _status = null;   // T.Starting so quando ja houver idioma

        public static void Show(string status)
        {
            lock (_lock)
            {
                _status = status ?? T.Starting;
                if (_thread != null && _thread.IsAlive) { Focar(); return; }

                string inicial = _status;
                _thread = new Thread(delegate()
                {
                    SplashForm f = new SplashForm();
                    f.Status = inicial;
                    lock (_lock) _form = f;
                    Application.Run(f);
                    lock (_lock) _form = null;
                });
                _thread.IsBackground = true;
                _thread.Name = "Splash";
                _thread.SetApartmentState(ApartmentState.STA);
                _thread.Start();
            }
        }

        public static void SetStatus(string status)
        {
            lock (_lock)
            {
                _status = status;
                SplashForm f = _form;
                if (f == null || f.IsDisposed) return;
                try { f.BeginInvoke(new MethodInvoker(delegate { f.Status = status; })); }
                catch (Exception ex) { Log.Error("estado da tela de carregamento", ex); }
            }
        }

        public static void Close()
        {
            lock (_lock)
            {
                SplashForm f = _form;
                if (f == null || f.IsDisposed) return;
                try { f.BeginInvoke(new MethodInvoker(delegate { f.Close(); })); }
                catch (Exception ex) { Log.Error("fechar tela de carregamento", ex); }
            }
        }

        private static void Focar()
        {
            SplashForm f = _form;
            if (f == null || f.IsDisposed) return;
            try { f.BeginInvoke(new MethodInvoker(delegate { f.Activate(); })); }
            catch { }
        }
    }

    public class SplashForm : Form
    {
        private readonly System.Windows.Forms.Timer _anim;   // System.Threading tambem tem um Timer
        private int _phase;
        private string _status = "";

        public string Status
        {
            get { return _status; }
            set
            {
                if (_status == value) return;
                _status = value ?? "";
                Invalidate();
                Update();          // a thread de UI esta bombeando na mao; nao espera o proximo ciclo
            }
        }

        public SplashForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            TopMost = true;
            ClientSize = new Size(380, 148);
            BackColor = Ui.Surface;
            Font = Ui.FontBase;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);

            using (GraphicsPath p = Ui.RoundRect(new Rectangle(0, 0, ClientSize.Width, ClientSize.Height), 10))
                Region = new Region(p);

            _anim = new System.Windows.Forms.Timer();
            _anim.Interval = 30;
            _anim.Tick += delegate { _phase = (_phase + 4) % (ClientSize.Width + 160); Invalidate(); };
            _anim.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _anim != null) { _anim.Stop(); _anim.Dispose(); }
            base.Dispose(disposing);
        }

        // ShowWithoutActivation era true aqui. Numa janela que precisa aceitar
        // clique, nao ativar so atrapalha: o primeiro clique ia para a ativacao
        // e nao para o botao de fechar.

        /// <summary>Area do "x" de fechar, no canto superior direito.</summary>
        private Rectangle BotaoFechar
        {
            get { return new Rectangle(ClientSize.Width - 34, 8, 26, 26); }
        }

        private bool _hoverFechar;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool h = BotaoFechar.Contains(e.Location);
            if (h != _hoverFechar) { _hoverFechar = h; Cursor = h ? Cursors.Hand : Cursors.Default; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { _hoverFechar = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (BotaoFechar.Contains(e.Location)) { Close(); return; }
            base.OnMouseDown(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Ui.Smooth(g);

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Ui.RoundRect(r, 10))
            using (SolidBrush b = new SolidBrush(Ui.Surface))
            using (Pen pen = new Pen(Ui.Border))
            {
                g.FillPath(b, p);
                g.DrawPath(pen, p);
            }

            try
            {
                if (Assets.AppIcon != null)
                    using (Bitmap logo = Assets.AppIcon.ToBitmap())
                        g.DrawImage(logo, 28, 30, 44, 44);
            }
            catch (Exception ex) { Log.Error("logo da tela de carregamento", ex); }

            TextRenderer.DrawText(g, "Mhiagos Control", Ui.FontTitle,
                new Rectangle(88, 28, Width - 110, 28), Ui.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            TextRenderer.DrawText(g, _status, Ui.FontBase,
                new Rectangle(88, 56, Width - 130, 22), Ui.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            Rectangle fechar = BotaoFechar;
            if (_hoverFechar)
                using (GraphicsPath p = Ui.RoundRect(fechar, 6))
                using (SolidBrush b = new SolidBrush(Ui.Hover))
                    g.FillPath(b, p);
            // U+E8BB (ChromeClose) escrito por codigo: o caractere literal no
            // fonte nao sobrevive a ida e volta pela pagina de codigo do build
            using (Font f = new Font("Segoe MDL2 Assets", 8f))
                TextRenderer.DrawText(g, "", f, fechar,
                    _hoverFechar ? Ui.Text : Ui.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            TextRenderer.DrawText(g, T.ClosingIsSafe, Ui.FontSmall,
                new Rectangle(88, 76, Width - 130, 18), Ui.Faint,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            // trilho e segmento deslizante
            int trackY = Height - 34, trackH = 4;
            Rectangle track = new Rectangle(28, trackY, Width - 56, trackH);
            using (GraphicsPath p = Ui.RoundRect(track, trackH / 2))
            using (SolidBrush b = new SolidBrush(Ui.SurfaceAlt))
                g.FillPath(b, p);

            int segW = 120;
            int x = 28 - segW + _phase;
            Rectangle seg = Rectangle.Intersect(track, new Rectangle(x, trackY, segW, trackH));
            if (seg.Width > 1)
                using (GraphicsPath p = Ui.RoundRect(seg, trackH / 2))
                using (SolidBrush b = new SolidBrush(Ui.Accent))
                    g.FillPath(b, p);
        }
    }
}
