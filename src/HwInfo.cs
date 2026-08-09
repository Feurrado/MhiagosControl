using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using LibreHardwareMonitor.Hardware;

namespace MhiagosControl
{
    /// <summary>Uma leitura vinda da biblioteca cliente do HWiNFO.</summary>
    public class HwReading
    {
        public string Id;        // "hw:<grupo>|<classe>|<rotulo>" - estavel entre sessoes
        public string Group;
        public string Label;
        public string Unit;
        public SensorType Type;
        public double Value;
    }

    /// <summary>
    /// Fonte de sensores baseada na biblioteca cliente do HWiNFO
    /// (api-ms-win-core-sysinfo-825-{32,64}.dll).
    ///
    /// A biblioteca exporta apenas por ordinal; a correspondencia abaixo foi
    /// recuperada do binario que a acompanha. Todas as funcoes sao cdecl:
    ///
    ///   850(0xC0)                      Init; devolve 0 em caso de sucesso
    ///   156()                          quantidade de grupos de sensores
    ///   263()                          chamada uma vez por ciclo, apos a contagem
    ///   678(i)                         prepara o grupo i
    ///   952(i, buf, tam)               nome do grupo i
    ///   641(classe, i, j, elem[0x1D0]) leitura j do grupo i; 0 encerra a serie
    ///
    /// No elemento de 464 bytes: valor double em +0x08, unidade em +0x10,
    /// categoria de hardware em +0x30 e rotulo em +0x148.
    ///
    /// Exige privilegio administrativo - sem ele o Init falha com codigo 1,
    /// porque a biblioteca precisa registrar e subir seu driver.
    /// </summary>
    public class HwInfo : IDisposable
    {
        private const int ELEM      = 0x1D0;
        private const int OFF_VALUE = 0x08;
        private const int OFF_UNIT  = 0x10;
        private const int OFF_LABEL = 0x148;
        private const int INIT_ARG  = 0xC0;

        private const int CLS_MIN = 1;
        private const int CLS_MAX = 8;

        [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr LoadLibraryA(string path);
        [DllImport("kernel32", SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr h, IntPtr ordinal);
        [DllImport("kernel32", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr h);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int D_void();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int D_i(int i);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int D_name(int i, byte[] buf, int size);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int D_read(int cls, int i, int j, byte[] elem);

        private IntPtr _lib = IntPtr.Zero;
        private D_i    _init;
        private D_void _count;
        private D_void _poll;
        private D_i    _select;
        private D_name _groupName;
        private D_read _read;

        private readonly byte[] _name = new byte[256];
        private readonly byte[] _elem = new byte[ELEM];

        public bool IsOpen { get { return _lib != IntPtr.Zero && _init != null; } }
        public string LoadedFrom { get; private set; }

        /// <summary>Nome do arquivo conforme o bitness do processo.</summary>
        private static string FileName
        {
            get { return "api-ms-win-core-sysinfo-825-" + (IntPtr.Size == 8 ? "64" : "32") + ".dll"; }
        }

        /// <summary>
        /// Locais procurados, em ordem. Preferimos uma copia ao lado do
        /// executavel para que o aplicativo nao dependa da instalacao original.
        /// </summary>
        private static IEnumerable<string> Candidates()
        {
            string f = FileName;
            yield return Path.Combine(Path.Combine(Paths.ExeDir, "engine"), f);
            yield return Path.Combine(Paths.ExeDir, f);
            yield return Path.Combine(@"C:\Program Files\CPU TEMP Monitor", f);
        }

        public bool Open()
        {
            foreach (string path in Candidates())
            {
                if (!File.Exists(path)) continue;
                if (TryLoad(path)) return true;
            }
            Log.Write("HWiNFO: " + FileName + " nao encontrada - seguindo sem essa fonte");
            return false;
        }

        private bool TryLoad(string path)
        {
            try
            {
                IntPtr h = LoadLibraryA(path);
                if (h == IntPtr.Zero)
                {
                    Log.Write("HWiNFO: LoadLibrary falhou em " + path + " (erro " + Marshal.GetLastWin32Error() + ")");
                    return false;
                }

                _lib       = h;
                _init      = Bind<D_i>(850);
                _count     = Bind<D_void>(156);
                _poll      = Bind<D_void>(263);
                _select    = Bind<D_i>(678);
                _groupName = Bind<D_name>(952);
                _read      = Bind<D_read>(641);

                int rc = _init(INIT_ARG);
                if (rc != 0)
                {
                    Log.Write("HWiNFO: Init falhou com codigo " + rc +
                              " (codigo 1 normalmente indica falta de elevacao)");
                    Close();
                    return false;
                }

                LoadedFrom = path;
                Log.Write("HWiNFO: motor iniciado a partir de " + path);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("HWiNFO: carga de " + path, ex);
                Close();
                return false;
            }
        }

        private T Bind<T>(int ordinal) where T : class
        {
            IntPtr p = GetProcAddress(_lib, new IntPtr(ordinal));
            if (p == IntPtr.Zero) throw new Exception("ordinal " + ordinal + " nao resolveu");
            return (T)(object)Marshal.GetDelegateForFunctionPointer(p, typeof(T));
        }

        /// <summary>
        /// Le todas as leituras disponiveis. A biblioteca nao expoe consulta
        /// individual, entao cada ciclo reenumera - o custo e uma copia de
        /// memoria por leitura, irrelevante na cadencia de um segundo.
        /// </summary>
        public List<HwReading> ReadAll()
        {
            List<HwReading> result = new List<HwReading>();
            if (!IsOpen) return result;

            try
            {
                int groups = _count();
                if (groups <= 0) return result;
                _poll();

                for (int i = 0; i < groups; i++)
                {
                    Array.Clear(_name, 0, _name.Length);
                    _select(i);
                    _groupName(i, _name, _name.Length);
                    string group = Ansi(_name, 0, _name.Length);
                    if (group.Length == 0) group = "Grupo " + i;

                    for (int cls = CLS_MIN; cls <= CLS_MAX; cls++)
                    {
                        for (int j = 0; j < 256; j++)
                        {
                            Array.Clear(_elem, 0, ELEM);
                            if (_read(cls, i, j, _elem) == 0) break;

                            double v = BitConverter.ToDouble(_elem, OFF_VALUE);
                            if (double.IsNaN(v) || double.IsInfinity(v)) continue;

                            string label = Ansi(_elem, OFF_LABEL, 128);
                            if (label.Length == 0) continue;
                            string unit = Ansi(_elem, OFF_UNIT, 16);

                            HwReading r = new HwReading();
                            r.Group = group;
                            r.Label = label;
                            r.Unit  = unit;
                            r.Value = v;
                            r.Type  = MapType(cls, unit);
                            r.Id    = "hw:" + group + "|" + cls + "|" + label;
                            result.Add(r);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("HWiNFO: leitura", ex);
            }
            return result;
        }

        /// <summary>
        /// Converte a classe do HWiNFO no tipo da LibreHardwareMonitor, para que
        /// unidades, escala e conversao para Fahrenheit sigam valendo sem ramo
        /// especial no resto do aplicativo.
        /// </summary>
        private static SensorType MapType(int cls, string unit)
        {
            switch (cls)
            {
                case 1: return SensorType.Temperature;
                case 2: return SensorType.Voltage;
                case 3: return SensorType.Fan;
                case 4: return SensorType.Current;
                case 5: return SensorType.Power;
                case 6: return SensorType.Clock;
                case 7: return SensorType.Load;
            }

            // A classe 8 mistura tudo; a unidade e o unico indicio confiavel.
            string u = (unit ?? "").Trim();
            if (u == "%")                          return SensorType.Load;
            if (u == "MB" || u == "GB")            return SensorType.Data;
            if (u == "MHz" || u == "GHz")          return SensorType.Clock;
            if (u == "W")                          return SensorType.Power;
            if (u == "V")                          return SensorType.Voltage;
            if (u == "A")                          return SensorType.Current;
            if (u == "RPM")                        return SensorType.Fan;
            if (u == "MB/s" || u == "KB/s")        return SensorType.Throughput;
            return SensorType.Factor;
        }

        private static string Ansi(byte[] b, int off, int max)
        {
            if (off < 0 || off >= b.Length) return "";
            int end = off;
            int limit = Math.Min(b.Length, off + max);
            while (end < limit && b[end] != 0) end++;
            return Encoding.Default.GetString(b, off, end - off).Trim();
        }

        public void Close()
        {
            _init = null; _count = null; _poll = null;
            _select = null; _groupName = null; _read = null;
            if (_lib != IntPtr.Zero) { try { FreeLibrary(_lib); } catch { } _lib = IntPtr.Zero; }
            LoadedFrom = null;
        }

        public void Dispose() { Close(); }
    }
}
