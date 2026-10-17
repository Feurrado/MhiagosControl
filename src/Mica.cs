using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// Mica (o material translucido do Windows 11) na barra de titulo da
    /// janela de configuracao. O resto da janela segue opaco.
    ///
    /// So o atributo de fundo do DWM (DWMWA_SYSTEMBACKDROP_TYPE): a barra de
    /// titulo passa a mostrar o material, e a area de cliente nao muda. Windows
    /// 10 (build abaixo de 22621), alto contraste ou qualquer falha: nada
    /// disso liga, e a barra de titulo e a de sempre. MHIAGOS_SEM_MICA desliga.
    ///
    /// Estender o quadro para a barra lateral (pixel preto vira transparente) foi
    /// tentado e ficou de fora: o texto GDI sobre area de vidro perde o
    /// antisserrilhado, e o resultado nao pode ser conferido numa janela inativa.
    /// </summary>
    internal static class Mica
    {
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
        private const int DWMSBT_MAINWINDOW = 2;
        private const int BuildMinima = 22621;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("ntdll.dll")]
        private static extern int RtlGetVersion(ref OSVERSIONINFO info);

        [StructLayout(LayoutKind.Sequential)]
        private struct OSVERSIONINFO
        {
            public int Size, Major, Minor, Build, Platform;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Csd;
        }

        /// <summary>O build do Windows aceita Mica e o usuario nao esta em alto contraste.</summary>
        public static bool Disponivel
        {
            get
            {
                try
                {
                    return Environment.GetEnvironmentVariable("MHIAGOS_SEM_MICA") == null
                        && Compativel(Build(), SystemInformation.HighContrast);
                }
                catch { return false; }
            }
        }

        internal static bool Compativel(int build, bool altoContraste)
        {
            return build >= BuildMinima && !altoContraste;
        }

        private static int Build()
        {
            try
            {
                OSVERSIONINFO v = new OSVERSIONINFO();
                v.Size = Marshal.SizeOf(typeof(OSVERSIONINFO));
                if (RtlGetVersion(ref v) == 0) return v.Build;
            }
            catch { }
            return Environment.OSVersion.Version.Build;
        }

        /// <summary>Liga o Mica na barra de titulo. Devolve se ligou.</summary>
        public static bool Aplicar(Form f)
        {
            if (f == null || !f.IsHandleCreated || !Disponivel) return false;
            try
            {
                int tipo = DWMSBT_MAINWINDOW;
                return DwmSetWindowAttribute(f.Handle, DWMWA_SYSTEMBACKDROP_TYPE, ref tipo, sizeof(int)) == 0;
            }
            catch (Exception ex) { Log.Error("mica", ex); return false; }
        }
    }
}
