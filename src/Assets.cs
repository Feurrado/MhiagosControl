using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// Ativos embutidos no executavel (icones e a vista superior do cooler).
    ///
    /// Ficam como recursos do assembly em vez de arquivos soltos: o executavel
    /// se basta, e nao ha como o usuario mover a pasta e quebrar a interface.
    /// </summary>
    public static class Assets
    {
        private static Icon _tray;
        private static Image _cooler;
        private static bool _coolerTried = false;

        /// <summary>Icone da bandeja, no tamanho que o Windows pede.</summary>
        public static Icon TrayIcon
        {
            get
            {
                if (_tray != null) return _tray;
                try
                {
                    using (Stream s = Open("tray.ico"))
                        if (s != null) _tray = new Icon(s, SystemInformation.SmallIconSize);
                }
                catch (Exception ex) { Log.Error("carregamento do icone da bandeja", ex); }
                return _tray;
            }
        }

        private static Icon _app;

        /// <summary>
        /// Icone das janelas.
        ///
        /// O /win32icon: do compilador define o icone do ARQUIVO (Explorer,
        /// barra de tarefas). O Form.Icon do WinForms e independente: sem
        /// atribuicao explicita a janela usa o icone padrao do framework, e e
        /// esse que aparece na miniatura da barra de tarefas.
        /// </summary>
        public static Icon AppIcon
        {
            get
            {
                if (_app != null) return _app;
                try
                {
                    using (Stream s = Open("app.ico"))
                        // Form.Icon fornece o HICON usado pelo Windows. Pedir
                        // 256 px ao leitor do .NET Framework nao seleciona essa
                        // entrada de forma confiavel; use o tamanho da janela.
                        if (s != null) _app = new Icon(s, SystemInformation.IconSize);
                }
                catch (Exception ex) { Log.Error("carregamento do icone do aplicativo", ex); }

                if (_app == null)
                {
                    try { _app = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location); }
                    catch { _app = TrayIcon; }
                }
                return _app;
            }
        }

        /// <summary>Vista superior do cooler, usada como fundo da previa.</summary>
        public static Image Cooler
        {
            get
            {
                if (_coolerTried) return _cooler;
                _coolerTried = true;
                try
                {
                    using (Stream s = Open("cooler.png"))
                        if (s != null) _cooler = Image.FromStream(s);
                }
                catch (Exception ex) { Log.Error("carregamento da imagem do cooler", ex); }
                return _cooler;
            }
        }

        private static Image _headset, _mouse, _bluetooth;
        private static bool _perifTried = false;

        /// <summary>Headset G325 branco, decalcado de foto (assets\svg\g325.svg).</summary>
        public static Image Headset { get { CarregarPerifericos(); return _headset; } }

        /// <summary>Mouse X11 branco, decalcado de foto (assets\svg\x11.svg).</summary>
        public static Image Mouse { get { CarregarPerifericos(); return _mouse; } }

        /// <summary>Simbolo do Bluetooth, para os aparelhos lidos pelo Windows.</summary>
        public static Image Bluetooth { get { CarregarPerifericos(); return _bluetooth; } }

        private static void CarregarPerifericos()
        {
            if (_perifTried) return;
            _perifTried = true;
            try
            {
                _headset = Copia("g325.png");
                _mouse = Copia("x11.png");
                _bluetooth = Copia("bluetooth.png");
            }
            catch (Exception ex) { Log.Error("carregamento das imagens dos perifericos", ex); }
        }

        /// <summary>
        /// Copia em memoria: o GDI+ volta a ler o fluxo de Image.FromStream
        /// quando desenha, e o recurso ja estaria fechado.
        /// </summary>
        private static Image Copia(string name)
        {
            using (Stream s = Open(name))
            {
                if (s == null) return null;
                using (Image img = Image.FromStream(s)) return new Bitmap(img);
            }
        }

        /// <summary>O app.ico embutido, em bytes, para gravar em disco (o Windows pede um arquivo).</summary>
        internal static byte[] AppIconBytes()
        {
            using (Stream s = Open("app.ico"))
            {
                if (s == null) return null;
                using (MemoryStream m = new MemoryStream()) { s.CopyTo(m); return m.ToArray(); }
            }
        }

        /// <summary>O icone do app num tamanho pedido, para a notificacao.</summary>
        internal static Icon AppIconDe(Size tamanho)
        {
            try
            {
                using (Stream s = Open("app.ico"))
                    if (s != null) return new Icon(s, tamanho);
            }
            catch (Exception ex) { Log.Error("icone do app em " + tamanho, ex); }
            return null;
        }

        private static Stream Open(string name)
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            foreach (string res in asm.GetManifestResourceNames())
                if (res.EndsWith(name, StringComparison.OrdinalIgnoreCase))
                    return asm.GetManifestResourceStream(res);
            return null;
        }
    }
}
