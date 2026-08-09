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
                        if (s != null) _app = new Icon(s, 256, 256);
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
