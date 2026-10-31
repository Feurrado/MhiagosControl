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
        /// <summary>A area de trabalho subiu para cima da janela? So acontece com o Mostrar area de trabalho.</summary>
        private static bool DesktopPorCima(IntPtr janela)
        {
            IntPtr desktop = AreaDeTrabalho();
            if (desktop == IntPtr.Zero) return false;
            int limite = 4096;
            for (IntPtr h = GetWindow(janela, GW_HWNDPREV); h != IntPtr.Zero && limite-- > 0; h = GetWindow(h, GW_HWNDPREV))
                if (h == desktop) return true;
            return false;
        }

        private bool DesktopPorCima() { return DesktopPorCima(Handle); }

        /// <summary>
        /// O lugar do widget na pilha de janelas, compartilhado com o widget da
        /// casa: fora do "sempre na frente", preso logo acima da area de
        /// trabalho e abaixo de todo o resto, como um gadget; com o Win+D sobe
        /// enquanto a area de trabalho estiver por cima. Nao rouba o foco.
        /// </summary>
        internal sealed class Camada : IDisposable
        {
            private readonly Form _janela;
            private readonly Func<bool> _naFrente;
            private readonly Timer _conferir = new Timer();
            private bool _sobreDesktop;
            private IntPtr _gancho = IntPtr.Zero;
            private WinEventDelegate _ganchoProc;

            public Camada(Form janela, Func<bool> naFrente)
            {
                _janela = janela;
                _naFrente = naFrente;
                _conferir.Interval = 300;
                _conferir.Tick += delegate { _conferir.Stop(); Conferir(); };
            }

            /// <summary>Mostrar a area de trabalho (Win+D) cobriu tudo; o widget subiu ate alguem voltar.</summary>
            public bool SobreDesktop { get { return _sobreDesktop; } }

            private bool Pronta { get { return !_janela.IsDisposed && _janela.IsHandleCreated; } }

            /// <summary>Na abertura: vai para o fundo e passa a ouvir as trocas de primeiro plano.</summary>
            public void Ligar()
            {
                AplicarFrente();
                try
                {
                    _ganchoProc = new WinEventDelegate(AoMudarPrimeiroPlano);
                    _gancho = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero,
                                              _ganchoProc, 0, 0, WINEVENT_OUTOFCONTEXT);
                }
                catch (Exception ex) { Log.Error("gancho de primeiro plano do widget", ex); }
            }

            /// <summary>
            /// Clique nao ativa a janela, e cada mudanca de ordem e trocada pelo
            /// lugar logo acima da area de trabalho. HWND_BOTTOM puro nao serve:
            /// ficaria atras da janela dos icones, que ocupa a tela inteira.
            /// Devolve true quando a mensagem ja foi respondida.
            /// </summary>
            public bool Mensagem(ref Message m)
            {
                if (m.Msg == WM_MOUSEACTIVATE)
                {
                    m.Result = (IntPtr)MA_NOACTIVATE;
                    return true;
                }
                if (m.Msg == WM_WINDOWPOSCHANGING && !_naFrente() && !_sobreDesktop && m.LParam != IntPtr.Zero)
                {
                    try
                    {
                        WINDOWPOS wp = (WINDOWPOS)Marshal.PtrToStructure(m.LParam, typeof(WINDOWPOS));
                        if ((wp.flags & SWP_NOZORDER) == 0)
                        {
                            IntPtr depois = LugarNoFundo();
                            if (depois == _janela.Handle) wp.flags |= SWP_NOZORDER;
                            else wp.hwndInsertAfter = depois;
                            Marshal.StructureToPtr(wp, m.LParam, false);
                        }
                    }
                    catch (Exception ex) { Log.Error("ordem do widget", ex); }
                }
                return false;
            }

            public void AplicarFrente()
            {
                if (!Pronta) return;
                _sobreDesktop = false;
                if (_naFrente()) SetWindowPos(_janela.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                else
                {
                    SetWindowPos(_janela.Handle, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                    Afundar();
                }
            }

            public void Afundar()
            {
                if (!Pronta || _naFrente()) return;
                // a ordem final vem do WM_WINDOWPOSCHANGING acima
                SetWindowPos(_janela.Handle, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }

            /// <summary>A janela atras da qual o widget deve ficar: a que esta logo acima da area de trabalho.</summary>
            private static IntPtr LugarNoFundo()
            {
                IntPtr desktop = AreaDeTrabalho();
                if (desktop == IntPtr.Zero) return HWND_BOTTOM;
                IntPtr acima = GetWindow(desktop, GW_HWNDPREV);
                if (acima == IntPtr.Zero) return HWND_TOP;   // a area de trabalho esta no topo: Win+D
                return acima;
            }

            private void AoMudarPrimeiroPlano(IntPtr gancho, uint evento, IntPtr hwnd, int objeto, int filho, uint thread, uint tempo)
            {
                if (!Pronta || _naFrente()) return;
                Conferir();
                // o Explorer as vezes termina de subir a area de trabalho depois de avisar
                _conferir.Stop();
                _conferir.Start();
            }

            /// <summary>
            /// Com o Win+D a area de trabalho cobre todas as janelas, inclusive o
            /// widget. Ele sobe para a frente enquanto ela estiver por cima e
            /// volta ao fundo quando qualquer outra janela ganha o primeiro plano.
            /// </summary>
            public void Conferir()
            {
                if (!Pronta || _naFrente()) return;
                try
                {
                    if (!_sobreDesktop && DesktopPorCima(_janela.Handle))
                    {
                        _sobreDesktop = true;
                        SetWindowPos(_janela.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                    }
                    else if (_sobreDesktop && !EhAreaDeTrabalho(GetForegroundWindow()))
                    {
                        _sobreDesktop = false;
                        SetWindowPos(_janela.Handle, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                        Afundar();
                    }
                }
                catch (Exception ex) { Log.Error("widget e area de trabalho", ex); }
            }

            public void Dispose()
            {
                _conferir.Stop(); _conferir.Dispose();
                if (_gancho != IntPtr.Zero) { UnhookWinEvent(_gancho); _gancho = IntPtr.Zero; }
            }
        }
        private static bool EhAreaDeTrabalho(IntPtr h)
        {
            if (h == IntPtr.Zero) return true;
            string c = NomeDaClasse(h);
            return c == "Progman" || c == "WorkerW";
        }

        private static bool Escondida(IntPtr h)
        {
            int cloaked = 0;
            try { DwmGetWindowAttribute(h, DWMWA_CLOAKED, out cloaked, sizeof(int)); }
            catch { }
            return cloaked != 0;
        }

        private static string NomeDaClasse(IntPtr h)
        {
            StringBuilder sb = new StringBuilder(64);
            GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        /// <summary>Uma linha sobre escala, posicao e lugar na pilha de janelas, para a previa e o log.</summary>
        internal string Diagnostico()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("windows 11: ").Append(Windows11 ? "sim" : "nao");
            sb.Append(" | escala ").Append(_escala.ToString("0.##", CultureInfo.InvariantCulture));
            sb.Append(" | ").Append(Width).Append('x').Append(Height).Append(" em ").Append(Left).Append(',').Append(Top);
            if (!IsHandleCreated) return sb.ToString();
            IntPtr desktop = AreaDeTrabalho();
            sb.Append(" | area de trabalho: ").Append(desktop == IntPtr.Zero ? "nao achada" : NomeDaClasse(desktop));
            if (desktop != IntPtr.Zero)
            {
                // janelas invisiveis ou escondidas pelo DWM (apps suspensos) nao
                // contam: o que importa e se ha algo visivel entre os dois
                int entre = 0, limite = 4096;
                IntPtr h = GetWindow(Handle, GW_HWNDNEXT);
                for (; h != IntPtr.Zero && h != desktop && limite-- > 0; h = GetWindow(h, GW_HWNDNEXT))
                    if (IsWindowVisible(h) && !Escondida(h)) entre++;
                if (h == desktop) sb.Append(" | janelas visiveis entre o widget e ela: ").Append(entre);
                else sb.Append(" | area de trabalho nao esta abaixo do widget");
            }
            sb.Append(" | ela por cima do widget: ").Append(DesktopPorCima() ? "sim" : "nao");
            sb.Append(" | sempre na frente: ").Append(_cfg.NaFrente || _camada.SobreDesktop ? "sim" : "nao");
            return sb.ToString();
        }

        /// <summary>
        /// Tema e tela mudando. O SystemEvents segura quem assina para sempre,
        /// por isso a assinatura mora num objeto proprio que se desfaz junto.
        /// </summary>
        private sealed class AvisosDoSistema : IDisposable
        {
            private readonly WidgetDeBateria _dono;
            private readonly Microsoft.Win32.UserPreferenceChangedEventHandler _pref;
            private readonly EventHandler _tela;

            public AvisosDoSistema(WidgetDeBateria dono)
            {
                _dono = dono;
                _pref = delegate(object s, Microsoft.Win32.UserPreferenceChangedEventArgs a)
                {
                    if (a.Category == Microsoft.Win32.UserPreferenceCategory.General ||
                        a.Category == Microsoft.Win32.UserPreferenceCategory.Color)
                        Depois(delegate { _dono.Renderizar(); });
                };
                _tela = delegate { Depois(delegate { _dono.ManterNaTela(); _dono.Renderizar(); }); };
                Microsoft.Win32.SystemEvents.UserPreferenceChanged += _pref;
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged += _tela;
            }

            /// <summary>O aviso pode vir de outra thread; o desenho e da thread da janela.</summary>
            private void Depois(MethodInvoker acao)
            {
                try
                {
                    if (_dono.IsDisposed || !_dono.IsHandleCreated) return;
                    _dono.BeginInvoke(acao);
                }
                catch (Exception ex) { Log.Error("aviso do sistema ao widget", ex); }
            }

            public void Dispose()
            {
                Microsoft.Win32.SystemEvents.UserPreferenceChanged -= _pref;
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= _tela;
            }
        }

        // ---------------- Win32 ----------------

        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TOOLWINDOW = 0x80;
        private const int WS_EX_NOACTIVATE = 0x8000000;
        private const int WS_EX_TOPMOST = 0x8;
        private const int WM_WINDOWPOSCHANGING = 0x46;
        private const int WM_MOUSEACTIVATE = 0x21;
        private const int MA_NOACTIVATE = 3;
        private const int ULW_ALPHA = 2;
        private const byte AC_SRC_OVER = 0;
        private const byte AC_SRC_ALPHA = 1;
        private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;
        private const uint GW_HWNDNEXT = 2, GW_HWNDPREV = 3;
        private const int DWMWA_CLOAKED = 14;
        private const uint EVENT_SYSTEM_FOREGROUND = 3;
        private const uint WINEVENT_OUTOFCONTEXT = 0;
        private const int WCA_ACCENT_POLICY = 19;
        private const int ACCENT_DISABLED = 0;
        private const int ACCENT_ENABLE_BLURBEHIND = 3;
        private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;
        private static readonly IntPtr HWND_TOP = IntPtr.Zero;
        private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE { public int Cx, Cy; public SIZE(int cx, int cy) { Cx = cx; Cy = cy; } }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWPOS
        {
            public IntPtr hwnd, hwndInsertAfter;
            public int x, y, cx, cy;
            public uint flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy { public int AccentState, AccentFlags; public uint GradientColor; public int AnimationId; }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData { public int Attribute; public IntPtr Data; public int SizeOfData; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OSVERSIONINFOEX
        {
            public int dwOSVersionInfoSize, dwMajorVersion, dwMinorVersion, dwBuildNumber, dwPlatformId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szCSDVersion;
            public ushort wServicePackMajor, wServicePackMinor, wSuiteMask;
            public byte wProductType, wReserved;
        }

        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUND = 2;

        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("ntdll.dll")] private static extern int RtlGetVersion(ref OSVERSIONINFOEX info);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);

        private delegate void WinEventDelegate(IntPtr hook, uint evento, IntPtr hwnd, int objeto, int filho, uint thread, uint tempo);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
            IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);
        [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hwnd, IntPtr rgn, bool redraw);
        [DllImport("user32.dll")] private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string cls, string nome);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr pai, IntPtr depois, string cls, string nome);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder sb, int max);
        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventDelegate proc, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    }
}
