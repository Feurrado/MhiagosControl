using System;
using System.Runtime.InteropServices;

namespace MhiagosControl
{
    /// <summary>
    /// Comunicacao com o painel do cooler (VID 1A2C / PID 4984).
    ///
    /// Protocolo levantado por engenharia reversa:
    ///   SET_REPORT (Feature), ReportID 0x07, wIndex=1, 64 bytes
    ///   software original: ~1,1 s; app: 250 ms (16 ms foi validado sem falhas)
    ///   Setup: 21 09 07 03 01 00 40 00
    ///
    ///   [0]     0x07
    ///   [1..3]  centena, dezena, unidade  -> painel 1 (000-999)
    ///   [4]     flags: bit0 (0x01) = Fahrenheit ; bit4 (0x10) = porcentagem
    ///           os outros seis bits foram varridos e nao fazem nada. Nao ha
    ///           como APAGAR os indicadores por aqui, so alternar dentro de
    ///           cada par - veja "Os seis bits inertes" no README.
    ///   [5..7]  centena, dezena, unidade  -> painel 2 (000-999)
    ///   [8..63] zeros
    ///
    /// O firmware tem watchdog: sem reenvio continuo o painel apaga.
    /// </summary>
    public class HidPanel : IPanelDevice
    {
        // Sem o painel, enumerar todas as colecoes HID a cada ciclo nao melhora
        // a reconexao e pode custar bastante em maquinas com muitos dispositivos.
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

        public const ushort VID = 0x1A2C;

        /// <summary>
        /// O PID do Temp 6 Pro Black, o aparelho em que o protocolo foi
        /// levantado. E preferido quando existe, mas nao e mais exigido: veja
        /// FindDevicePath.
        /// </summary>
        public const ushort PID = 0x4984;

        public const ushort USAGE_PAGE = 0xFF01;

        /// <summary>VID:PID do painel encontrado, para a aba Sobre e o log.</summary>
        public static string UltimoIdentificado { get; private set; }

        public const byte FLAG_FAHRENHEIT = 0x01;
        public const byte FLAG_PERCENT = 0x10;

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDD_ATTRIBUTES
        {
            public int Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDP_CAPS
        {
            public ushort Usage; public ushort UsagePage;
            public ushort InputReportByteLength; public ushort OutputReportByteLength; public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort a1, a2, a3, a4, a5, a6, a7, a8, a9;
        }

        [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid g);
        [DllImport("hid.dll")] private static extern bool HidD_GetAttributes(IntPtr h, ref HIDD_ATTRIBUTES a);
        [DllImport("hid.dll")] private static extern bool HidD_GetPreparsedData(IntPtr h, out IntPtr p);
        [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr p);
        [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr p, out HIDP_CAPS c);
        [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_SetFeature(IntPtr h, byte[] buf, int len);

        [DllImport("setupapi.dll", CharSet = CharSet.Auto)] private static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr h, int f);
        [DllImport("setupapi.dll", CharSet = CharSet.Auto)] private static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr d, ref Guid g, int i, ref SP_DEVICE_INTERFACE_DATA a);
        [DllImport("setupapi.dll", CharSet = CharSet.Auto)] private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr s, ref SP_DEVICE_INTERFACE_DATA a, IntPtr d, int sz, ref int req, IntPtr b);
        [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CreateFile(string n, uint acc, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);

        private string _path;
        private IntPtr _handle = (IntPtr)(-1);
        private DateTime _nextAutomaticOpen = DateTime.MinValue;

        public string DevicePath { get { return _path; } }
        public bool IsConnected { get { return _handle.ToInt64() != -1; } }

        /// <summary>
        /// Localiza a coleccao vendor-defined FF01 do cooler.
        ///
        /// Aceita QUALQUER PID do fabricante (VID 1A2C, Shenzhen Shinetek), e
        /// nao so o 4984 do Temp 6 Pro Black. A mesma placa controladora sai em
        /// varios modelos da linha, e exigir o PID exato faria o aplicativo nao
        /// enxergar um aparelho que ele saberia comandar. O que identifica o
        /// painel de verdade e a coleccao FF01: nenhum dispositivo comum a expoe.
        ///
        /// O PID conhecido continua tendo preferencia - se ele estiver presente,
        /// e esse que vai. So na ausencia dele um irmao e adotado, e o VID:PID
        /// encontrado fica no log e na aba Sobre, que e o que permite alguem
        /// relatar um modelo novo.
        /// </summary>
        public static string FindDevicePath()
        {
            Guid g; HidD_GetHidGuid(out g);
            IntPtr set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x12);
            if (set.ToInt64() == -1) return null;

            string found = null;
            string alternativo = null;
            string idAlternativo = null;
            try
            {
                for (int i = 0; found == null; i++)
                {
                    SP_DEVICE_INTERFACE_DATA did = new SP_DEVICE_INTERFACE_DATA();
                    did.cbSize = Marshal.SizeOf(did);
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref did)) break;

                    int req = 0;
                    SetupDiGetDeviceInterfaceDetail(set, ref did, IntPtr.Zero, 0, ref req, IntPtr.Zero);
                    if (req <= 0) continue;

                    IntPtr buf = Marshal.AllocHGlobal(req);
                    try
                    {
                        Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                        if (!SetupDiGetDeviceInterfaceDetail(set, ref did, buf, req, ref req, IntPtr.Zero)) continue;
                        string path = Marshal.PtrToStringAuto((IntPtr)(buf.ToInt64() + 4));

                        IntPtr h = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                        if (h.ToInt64() == -1) continue;
                        try
                        {
                            HIDD_ATTRIBUTES attr = new HIDD_ATTRIBUTES();
                            attr.Size = Marshal.SizeOf(attr);
                            if (!HidD_GetAttributes(h, ref attr)) continue;
                            if (attr.VendorID != VID) continue;

                            IntPtr pp;
                            if (!HidD_GetPreparsedData(h, out pp)) continue;
                            try
                            {
                                HIDP_CAPS caps;
                                if (HidP_GetCaps(pp, out caps) != 0x110000 || caps.UsagePage != USAGE_PAGE)
                                    continue;

                                if (attr.ProductID == PID) found = path;
                                else if (alternativo == null)
                                {
                                    alternativo = path;
                                    idAlternativo = Identificar(attr.VendorID, attr.ProductID);
                                }
                            }
                            finally { HidD_FreePreparsedData(pp); }
                        }
                        finally { CloseHandle(h); }
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }

            if (found != null)
            {
                UltimoIdentificado = Identificar(VID, PID);
                return found;
            }
            if (alternativo != null)
            {
                UltimoIdentificado = idAlternativo;
                // Nao e o aparelho em que o protocolo foi levantado. Registrar
                // qual e o unico jeito de alguem relatar um modelo que funciona.
                Log.Write("painel: mesmo fabricante, modelo diferente do testado (" +
                          idAlternativo + ") - comandando assim mesmo");
                return alternativo;
            }
            UltimoIdentificado = null;
            return null;
        }

        private static string Identificar(ushort vid, ushort pid)
        {
            return "VID " + vid.ToString("X4") + " / PID " + pid.ToString("X4");
        }

        /// <summary>Abre o dispositivo. Retorna false se o cooler nao estiver presente.</summary>
        public bool Open()
        {
            Close();
            _path = FindDevicePath();
            if (_path == null) return false;

            // GENERIC_READ|GENERIC_WRITE, compartilhado
            _handle = CreateFile(_path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (_handle.ToInt64() == -1)
                _handle = CreateFile(_path, 0x40000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (IsConnected) _nextAutomaticOpen = DateTime.MinValue;
            return IsConnected;
        }

        /// <summary>Abre pelo ciclo normal, respeitando a espera apos uma falha.</summary>
        private bool OpenAutomatically()
        {
            if (DateTime.UtcNow < _nextAutomaticOpen) return false;
            if (Open()) return true;
            _nextAutomaticOpen = DateTime.UtcNow.Add(RetryDelay);
            return false;
        }

        public void Close()
        {
            if (IsConnected) { CloseHandle(_handle); _handle = (IntPtr)(-1); }
        }

        /// <summary>
        /// Codigo que apaga um digito (nenhum segmento aceso).
        ///
        /// Descoberto empiricamente: qualquer valor de 0x0A a 0x0F apaga o
        /// digito. Da um estado "sem leitura" de verdade, em vez de mostrar
        /// zero, que seria confundido com uma medicao valida.
        /// </summary>
        public const byte DIGIT_BLANK = 0x0A;

        /// <summary>
        /// Envia um quadro ao painel. Passe null para apagar o mostrador.
        /// Valores fora de 0..999 sao limitados.
        /// Retorna false se o envio falhar (dispositivo removido, por exemplo).
        /// </summary>
        public bool Send(int? panel1, int? panel2, bool fahrenheit, bool percent)
        {
            if (!IsConnected && !OpenAutomatically()) return false;

            byte flags = 0;
            if (fahrenheit) flags |= FLAG_FAHRENHEIT;
            if (percent) flags |= FLAG_PERCENT;

            byte[] b = new byte[64];
            b[0] = 0x07;
            WriteField(b, 1, panel1);
            b[4] = flags;
            WriteField(b, 5, panel2);

            bool ok = HidD_SetFeature(_handle, b, 64);
            if (!ok)
            {
                // dispositivo pode ter sido removido; forca reabertura na proxima chamada
                Close();
                _nextAutomaticOpen = DateTime.UtcNow.Add(RetryDelay);
            }
            return ok;
        }

        /// <summary>
        /// Envia um quadro cru de 64 bytes, sem interpretar nada.
        ///
        /// Existe para sondagem: os codigos de digito conhecidos vao ate 0x0F e
        /// so dois bits de report[4] tem significado levantado. O resto do
        /// espaco do protocolo nunca foi varrido. Nao e usado pelo aplicativo.
        /// </summary>
        public bool SendRaw(byte[] frame)
        {
            if (frame == null || frame.Length != 64) throw new ArgumentException("o quadro tem 64 bytes");
            if (!IsConnected && !Open()) return false;

            byte[] copy = (byte[])frame.Clone();   // HidD_SetFeature pode escrever no buffer
            bool ok = HidD_SetFeature(_handle, copy, 64);
            if (!ok) Close();
            return ok;
        }

        /// <summary>Escreve centena, dezena e unidade a partir de 'offset'.</summary>
        private static void WriteField(byte[] b, int offset, int? value)
        {
            if (!value.HasValue)
            {
                b[offset] = DIGIT_BLANK;
                b[offset + 1] = DIGIT_BLANK;
                b[offset + 2] = DIGIT_BLANK;
                return;
            }

            int v = value.Value;
            if (v < 0) v = 0;
            if (v > 999) v = 999;

            b[offset] = (byte)((v / 100) % 10);
            b[offset + 1] = (byte)((v / 10) % 10);
            b[offset + 2] = (byte)(v % 10);
        }
    }
}
