using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// Notificacao da bandeja com o icone do app no corpo.
    ///
    /// O ShowBalloonTip do WinForms so conhece os icones de sistema (aviso,
    /// erro, informacao) ou nenhum. O Shell_NotifyIcon aceita um icone proprio
    /// (NIIF_USER com hBalloonIcon), que o Windows 10 e 11 mostram grande no
    /// aviso. O NotifyIcon guarda a janela e o id dele em campos privados;
    /// sem eles (outra versao do framework), Mostrar devolve false e quem
    /// chamou usa o balao comum.
    /// </summary>
    internal static class AvisoComIcone
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public int uFlags;
            public int uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
            public int dwState;
            public int dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
            public int uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
            public int dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIcon(int message, ref NOTIFYICONDATA data);

        private const int NIM_MODIFY = 1;
        private const int NIF_INFO = 0x10;
        private const int NIIF_USER = 0x4;
        private const int NIIF_LARGE_ICON = 0x20;

        /// <summary>Guardado para sempre: o Windows le o HICON depois que a chamada volta.</summary>
        private static Icon _grande;

        public static bool Mostrar(NotifyIcon icone, string titulo, string texto)
        {
            try
            {
                FieldInfo fId = typeof(NotifyIcon).GetField("id", BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo fJanela = typeof(NotifyIcon).GetField("window", BindingFlags.NonPublic | BindingFlags.Instance);
                if (fId == null || fJanela == null) return false;
                NativeWindow janela = fJanela.GetValue(icone) as NativeWindow;
                if (janela == null || janela.Handle == IntPtr.Zero) return false;

                if (_grande == null)
                {
                    int lado = SystemInformation.IconSize.Width;   // NIIF_LARGE_ICON pede o tamanho SM_CXICON
                    _grande = Assets.AppIconDe(new Size(lado, lado)) ?? Assets.AppIcon;
                }
                if (_grande == null) return false;

                NOTIFYICONDATA d = new NOTIFYICONDATA();
                d.cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA));
                d.hWnd = janela.Handle;
                d.uID = (int)fId.GetValue(icone);
                d.uFlags = NIF_INFO;
                d.szInfoTitle = Cortar(titulo, 63);
                d.szInfo = Cortar(texto, 255);
                d.uTimeoutOrVersion = 10000;
                d.dwInfoFlags = NIIF_USER | NIIF_LARGE_ICON;
                d.hBalloonIcon = _grande.Handle;
                return Shell_NotifyIcon(NIM_MODIFY, ref d);
            }
            catch (Exception ex)
            {
                Log.Error("aviso com icone do app", ex);
                return false;
            }
        }

        private static string Cortar(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return " ";
            return s.Length <= max ? s : s.Substring(0, max);
        }
    }
}
