using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MhiagosControl
{
    /// <summary>
    /// Integracao com o tema do Windows.
    ///
    /// Os controles do kit (Card, FlatBtn, Toggle, SearchBox, NavBar,
    /// SensorPicker) se pintam sozinhos e NAO devem ser tocados aqui - foi
    /// justamente isso que quebrava a caixa de busca, com a borda do sistema
    /// sendo imposta sobre o campo arredondado.
    ///
    /// Sobra o que o WinForms nao deixa desenhar: barra de titulo e barras de
    /// rolagem, ambas resolvidas por API do Windows.
    /// </summary>
    public static class Theme
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string subAppName, string subIdList);

        // Ordinal 135 do uxtheme: define o modo preferido do processo. Nao e
        // documentado, mas e o unico caminho para barras de rolagem escuras em
        // controles nativos. Envolvido em try/catch por isso.
        [DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true)]
        private static extern int SetPreferredAppMode(int mode);

        [DllImport("uxtheme.dll", EntryPoint = "#136")]
        private static extern void FlushMenuThemes();

        private const int APPMODE_FORCE_DARK = 2;
        private const int APPMODE_ALLOW_DARK = 1;

        private static bool _appModeSet = false;

        private static bool _dark;
        private static bool _darkRead = false;

        /// <summary>
        /// Preferencia do Windows (Configuracoes &gt; Cores &gt; Modo do aplicativo).
        ///
        /// EM CACHE de proposito. Cada cor do kit consulta esta propriedade, e
        /// no desenho de uma lista isso acontece varias vezes por linha: ler o
        /// registro a cada chamada tornava a interface visivelmente lenta.
        /// A troca de tema pelo usuario dispara UserPreferenceChanged, que
        /// invalida o cache.
        /// </summary>
        public static bool IsDark
        {
            get
            {
                if (!_darkRead) { _dark = ReadDark(); _darkRead = true; }
                return _dark;
            }
        }

        private static bool ReadDark()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (k == null) return false;
                    object v = k.GetValue("AppsUseLightTheme");
                    if (v == null) return false;
                    return Convert.ToInt32(v) == 0;
                }
            }
            catch { return false; }
        }

        /// <summary>Relê a preferencia; chamar quando o Windows avisar que mudou.</summary>
        public static void Invalidate()
        {
            _darkRead = false;
        }

        /// <summary>Chamar uma vez no arranque, antes de criar janelas.</summary>
        public static void InitProcess()
        {
            if (_appModeSet) return;
            _appModeSet = true;
            try
            {
                SetPreferredAppMode(IsDark ? APPMODE_FORCE_DARK : APPMODE_ALLOW_DARK);
                FlushMenuThemes();
            }
            catch (Exception ex) { Log.Error("modo escuro do processo", ex); }

            try
            {
                Microsoft.Win32.SystemEvents.UserPreferenceChanged +=
                    delegate(object s, Microsoft.Win32.UserPreferenceChangedEventArgs a)
                    {
                        if (a.Category == Microsoft.Win32.UserPreferenceCategory.General ||
                            a.Category == Microsoft.Win32.UserPreferenceCategory.Color)
                            Invalidate();
                    };
            }
            catch (Exception ex) { Log.Error("monitor de preferencias", ex); }
        }

        public static void Apply(Form form)
        {
            form.BackColor = Ui.Window;
            form.ForeColor = Ui.Text;

            if (IsDark)
            {
                try
                {
                    int on = 1;
                    if (DwmSetWindowAttribute(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int)) != 0)
                        DwmSetWindowAttribute(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref on, sizeof(int));
                }
                catch (Exception ex) { Log.Error("barra de titulo escura", ex); }
            }

            ApplyScrollbars(form);
        }

        /// <summary>
        /// Aplica o tema escuro as barras de rolagem nativas.
        ///
        /// "DarkMode_Explorer" e o tema visual que o proprio Explorer usa; sem
        /// ele as barras saem brancas mesmo numa janela escura, que era o
        /// contraste gritante na lista de sensores.
        /// </summary>
        public static void ApplyScrollbars(Control parent)
        {
            if (!IsDark) return;
            try
            {
                foreach (Control c in parent.Controls)
                {
                    if (c is ListBox || c is ListView || c is TreeView || c is TextBox || c is Panel)
                    {
                        try { if (c.IsHandleCreated) SetWindowTheme(c.Handle, "DarkMode_Explorer", null); }
                        catch { }
                    }
                    if (c.Controls.Count > 0) ApplyScrollbars(c);
                }
            }
            catch (Exception ex) { Log.Error("tema das barras de rolagem", ex); }
        }
    }
}
