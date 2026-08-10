using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using LibreHardwareMonitor.Hardware;

namespace MhiagosControl
{
    /// <summary>Um sensor disponivel para exibicao, com identificador estavel.</summary>
    public class SensorEntry
    {
        public string Id;          // ISensor.Identifier, "hw:..." ou "synth:..."
        public string Hardware;    // nome do dispositivo, como a fonte o publica
        public string Name;        // nome curto do sensor

        /// <summary>
        /// Rotulo curto do tipo de dispositivo - CPU, GPU, Disco...
        ///
        /// O nome cru do hardware nao serve para agrupar nem para filtrar: vem
        /// "AMD Ryzen 5 5600X", "NVIDIA GeForce RTX 3060", "ASUS PRIME B550M-A
        /// (Nuvoton NCT6798D)". Numa lista de 500 px isso vira parágrafo, e como
        /// filtro nao cabe na tela.
        /// </summary>
        public string Category = "Outros";
        public string Label;       // texto completo, para listas simples
        public SensorType Type;
        public float? Value;

        /// <summary>Origem da leitura, para diagnostico e para a interface.</summary>
        public string Source = "LibreHardwareMonitor";

        /// <summary>Quantos sensores este agregado resume; 1 quando e um sensor real.</summary>
        public int Members = 1;

        /// <summary>
        /// Unidade informada pela fonte, quando ela informa.
        ///
        /// O tipo sozinho nao basta: SensorType.Data virava sempre "GB", mas o
        /// HWiNFO publica memoria em MB - "Physical Memory Used 11914" aparecia
        /// como 11914 GB. Quem sabe a unidade e quem produziu a leitura.
        /// </summary>
        public string Unit;

        /// <summary>Valor formatado com a unidade da fonte ou, na falta dela, a do tipo.</summary>
        public string Formatted
        {
            get
            {
                if (!Value.HasValue || float.IsNaN(Value.Value) || float.IsInfinity(Value.Value)) return "-";
                string u = string.IsNullOrEmpty(Unit)
                    ? Sensors.UnitOf(Type).Replace(", ", " ")
                    : " " + Unit;

                // Gigabyte sempre com uma casa: "12 GB" e "12.4 GB" lado a lado
                // na mesma lista fazem a coluna dancar, e em GB a casa decimal
                // carrega informacao - meio giga nao e ruido.
                string fmt = u.EndsWith("GB", StringComparison.Ordinal) ? "0.0" : "0.#";
                return Value.Value.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture) + u;
            }
        }

        public override string ToString() { return Label; }
    }

    /// <summary>Faz o driver visitar todo o hardware para atualizar leituras.</summary>
    internal class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) { computer.Traverse(this); }
        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (IHardware sub in hardware.SubHardware) sub.Accept(this);
        }
        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }

    /// <summary>
    /// Camada de leitura de sensores sobre duas fontes complementares.
    ///
    /// A LibreHardwareMonitor cobre GPU, uso de CPU, memoria, disco e rede sem
    /// driver proprio. Temperatura, potencia e clock real do processador exigem
    /// acesso em modo kernel: o driver que a biblioteca usa para isso e barrado
    /// pelo Windows por constar na lista de drivers vulneraveis, e nesses
    /// sensores ela devolve zero. A biblioteca cliente do HWiNFO cobre
    /// exatamente essa lacuna, com driver assinado que o sistema aceita.
    ///
    /// As duas fontes convivem: cada uma publica seus sensores com identificador
    /// proprio e o aplicativo funciona com qualquer subconjunto disponivel.
    /// Ambas exigem privilegio administrativo.
    /// </summary>
    public class Sensors : IDisposable
    {
        private Computer _computer;
        private HwInfo _hw;
        private readonly UpdateVisitor _visitor = new UpdateVisitor();

        /// <summary>Quando falso, sensores por nucleo sao resumidos em medias.</summary>
        public static bool ShowAll = false;

        /// <summary>Leituras do ciclo corrente, de todas as fontes.</summary>
        private List<SensorEntry> _raw = new List<SensorEntry>();

        // id sintetico -> identificadores dos sensores que ele resume
        private readonly Dictionary<string, List<string>> _synth = new Dictionary<string, List<string>>();

        private const string SynthPrefix = "synth:";

        /// <summary>
        /// Marcas de indice por nucleo. Cobre os dois estilos de nomenclatura:
        /// "CPU Core #1" da LibreHardwareMonitor e "Core 0 T1 Usage" ou
        /// "Core 0 Clock (perf #3/4)" do HWiNFO.
        /// </summary>
        private static readonly Regex CoreIndex = new Regex(
            @"#\s*\d+|\bCore\s+\d+\b|\bT\d\b|\(perf[^)]*\)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public bool HwInfoActive { get { return _hw != null && _hw.IsOpen; } }

        public void Open()
        {
            try
            {
                HwInfo hw = new HwInfo();
                if (hw.Open()) _hw = hw; else hw.Dispose();
            }
            catch (Exception ex) { Log.Error("HWiNFO: abertura", ex); _hw = null; }

            // A LibreHardwareMonitor so entra quando o HWiNFO nao esta disponivel.
            // Abri-la cobra um preco fixo: ela tenta instalar seu driver de kernel
            // a cada inicializacao e, como esse driver consta na lista de bloqueio
            // do Windows, o antivirus o remove e emite um alerta - toda vez. Com o
            // HWiNFO ativo nao ha o que ganhar pagando isso, porque ele entrega os
            // mesmos sensores (e mais alguns) por um driver que o sistema aceita.
            if (!HwInfoActive) OpenLibre();

            if (_computer == null && !HwInfoActive)
                throw new Exception("nenhuma fonte de sensores disponivel");

            // Duas passagens: varios sensores da LibreHardwareMonitor so publicam
            // valor a partir da segunda leitura. Sem isso, a lista inicial nascia
            // sem a temperatura do processador.
            Refresh();
            Refresh();
            BuildSynthetics();

            Log.Write("fontes ativas: " +
                      (_computer != null ? "LibreHardwareMonitor" : "-") + " | " +
                      (HwInfoActive ? "HWiNFO" : "-"));
        }

        /// <summary>Abre a LibreHardwareMonitor, recuando para um conjunto minimo se falhar.</summary>
        private void OpenLibre()
        {
            try
            {
                _computer = Build(true, true, true);
                _computer.Open();
            }
            catch (Exception)
            {
                SafeCloseComputer();
                try
                {
                    _computer = Build(false, false, false);
                    _computer.Open();
                }
                catch (Exception ex)
                {
                    SafeCloseComputer();
                    Log.Error("LibreHardwareMonitor: abertura", ex);
                }
            }
        }

        private static Computer Build(bool motherboard, bool memory, bool storage)
        {
            Computer c = new Computer();
            c.IsCpuEnabled = true;
            c.IsGpuEnabled = true;
            c.IsMotherboardEnabled = motherboard;
            c.IsMemoryEnabled = memory;
            c.IsStorageEnabled = storage;
            c.IsControllerEnabled = false;   // exigiria HidSharp
            return c;
        }

        private void SafeCloseComputer()
        {
            if (_computer != null) { try { _computer.Close(); } catch { } _computer = null; }
        }

        /// <summary>Atualiza as duas fontes e recompoe o instantaneo do ciclo.</summary>
        public void Refresh()
        {
            if (_computer != null)
            {
                try { _computer.Accept(_visitor); }
                catch (Exception ex) { Log.Error("LibreHardwareMonitor: atualizacao", ex); }
            }
            _raw = BuildRaw();
        }

        // ---------------- enumeracao ----------------

        private void ForEachSensor(Action<IHardware, ISensor> fn)
        {
            if (_computer == null) return;
            foreach (IHardware hw in _computer.Hardware)
            {
                foreach (ISensor s in hw.Sensors) fn(hw, s);
                foreach (IHardware sub in hw.SubHardware)
                    foreach (ISensor s in sub.Sensors) fn(sub, s);
            }
        }

        /// <summary>
        /// Junta as leituras das duas fontes numa lista unica.
        ///
        /// Inclui sensores momentaneamente sem leitura: filtra-los fazia sensores
        /// validos desaparecerem da lista so porque nao tinham valor no instante
        /// em que ela foi montada.
        /// </summary>
        private List<SensorEntry> BuildRaw()
        {
            List<SensorEntry> raw = new List<SensorEntry>();

            ForEachSensor(delegate(IHardware hw, ISensor s)
            {
                SensorEntry e = new SensorEntry();
                e.Id = s.Identifier.ToString();
                e.Hardware = hw.Name;
                e.Category = CategoryOf(hw.HardwareType);
                e.Name = s.Name;
                e.Type = s.SensorType;
                e.Value = s.Value;
                e.Label = Describe(hw.Name, s.Name, s.SensorType);
                raw.Add(e);
            });

            if (HwInfoActive)
            {
                foreach (HwReading r in _hw.ReadAll())
                {
                    SensorEntry e = new SensorEntry();
                    e.Id = r.Id;
                    e.Hardware = r.Group;
                    e.Category = CategoryOf(r.Category, r.Group);
                    e.Name = r.Label;
                    e.Type = r.Type;
                    e.Value = (float)r.Value;
                    e.Unit = CleanUnit(r.Unit);
                    ParaGigabytes(e);
                    e.Source = "HWiNFO";
                    e.Label = Describe(r.Group, r.Label, r.Type);
                    raw.Add(e);
                }
            }
            return raw;
        }

        /// <summary>
        /// Deixa apresentavel a unidade que vem da biblioteca.
        ///
        /// Ela chega em ANSI, entao o grau pode voltar como byte solto ou como
        /// "?". O resto do aplicativo escreve temperatura so com a letra, e a
        /// unidade vazia devolve a decisao ao tipo do sensor.
        /// </summary>
        private static string CleanUnit(string u)
        {
            if (string.IsNullOrEmpty(u)) return null;
            u = u.Trim();
            u = u.Replace("°", "").Replace("º", "").Replace("?", "").Trim();
            if (u.Length == 0) return null;
            if (u.Length > 8) return null;      // texto, nao unidade
            return u;
        }

        /// <summary>
        /// Converte leitura de memoria de MB para GB.
        ///
        /// O HWiNFO publica memoria em megabytes, e o numero cru nao diz nada:
        /// "Physical Memory Used 11930" exige conta de cabeca. Pior, passa dos
        /// 999 que o mostrador aceita, entao ia para o painel truncado. Em GB
        /// cabe e se le de imediato.
        /// </summary>
        private static void ParaGigabytes(SensorEntry e)
        {
            if (e.Unit != "MB" || !e.Value.HasValue) return;
            e.Value = e.Value.Value / 1024f;
            e.Unit = "GB";
        }

        // ---------------- categorias ----------------

        /// <summary>
        /// A ordem em que as categorias aparecem na lista e nos filtros. Fixa,
        /// e nao a ordem em que o hardware foi descoberto, para que a interface
        /// nao mude de arranjo entre maquinas nem entre execucoes.
        /// </summary>
        public static readonly string[] Categories = new string[]
        {
            "CPU", "GPU", "Placa-mãe", "Memória", "Disco", "Rede", "Outros"
        };

        private static string CategoryOf(HardwareType t)
        {
            switch (t)
            {
                case HardwareType.Cpu: return "CPU";
                case HardwareType.GpuNvidia:
                case HardwareType.GpuAmd:
                case HardwareType.GpuIntel: return "GPU";
                case HardwareType.Memory: return "Memória";
                case HardwareType.Storage: return "Disco";
                case HardwareType.Network: return "Rede";
                case HardwareType.Motherboard:
                case HardwareType.SuperIO:
                case HardwareType.EmbeddedController: return "Placa-mãe";
                default: return "Outros";
            }
        }

        /// <summary>
        /// Categoria de uma leitura do HWiNFO.
        ///
        /// O codigo vem do campo em +0x30 do elemento, recuperado por engenharia
        /// reversa - por isso ha o desempate pelo nome do grupo: se a biblioteca
        /// devolver um codigo fora dos conhecidos, o nome ainda classifica.
        /// </summary>
        private static string CategoryOf(int code, string group)
        {
            switch (code)
            {
                case 11: return "CPU";
                case 12: return "Placa-mãe";
                case 13: return "GPU";
                case 15: return "Disco";
                case 16: return "Rede";
            }
            return CategoryByName(group);
        }

        private static string CategoryByName(string group)
        {
            if (string.IsNullOrEmpty(group)) return "Outros";
            string g = group.ToUpperInvariant();

            if (Has(g, "GEFORCE", "RADEON", "NVIDIA", "GPU", "ARC A")) return "GPU";
            if (Has(g, "RYZEN", "CORE I", "ATHLON", "XEON", "THREADRIPPER", "CPU")) return "CPU";
            if (Has(g, "NVME", "SSD", "HDD", "DISK", "DRIVE", "WDC ", "SEAGATE")) return "Disco";
            if (Has(g, "DIMM", "MEMORY", "MEMÓRIA", "RAM")) return "Memória";
            if (Has(g, "ETHERNET", "WI-FI", "WIFI", "WIRELESS", "NETWORK", "LAN")) return "Rede";
            if (Has(g, "NUVOTON", "ITE IT", "ASUS", "GIGABYTE", "MSI", "ASROCK", "CHIPSET")) return "Placa-mãe";
            return "Outros";
        }

        private static bool Has(string haystack, params string[] needles)
        {
            foreach (string n in needles) if (haystack.IndexOf(n, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        private static string Describe(string hardware, string name, SensorType type)
        {
            return string.Format("{0} - {1} ({2}{3})", hardware, name, type, UnitOf(type));
        }

        /// <summary>Leituras do ciclo corrente, montando-as se ainda nao existirem.</summary>
        private List<SensorEntry> Snap()
        {
            if (_raw == null || _raw.Count == 0) _raw = BuildRaw();
            return _raw;
        }

        /// <summary>Lista os sensores para a interface.</summary>
        public List<SensorEntry> List()
        {
            List<SensorEntry> raw = Snap();
            List<SensorEntry> result = ShowAll ? new List<SensorEntry>(raw) : Condense(raw);
            result.Sort(delegate(SensorEntry a, SensorEntry b)
            {
                int c = string.Compare(a.Hardware, b.Hardware, StringComparison.OrdinalIgnoreCase);
                if (c != 0) return c;
                c = a.Type.CompareTo(b.Type);
                if (c != 0) return c;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            return result;
        }

        /// <summary>Nome sem o indice do nucleo: "Core #3 (SMU)" -> "Core (SMU)".</summary>
        private static string Normalize(string name)
        {
            string n = CoreIndex.Replace(name ?? "", "");
            return Regex.Replace(n, @"\s{2,}", " ").Trim();
        }

        private static bool IsPerCore(string name)
        {
            return !string.IsNullOrEmpty(name) && CoreIndex.IsMatch(name);
        }

        private static string GroupKey(SensorEntry e)
        {
            return e.Hardware + "|" + e.Type + "|" + Normalize(e.Name);
        }

        /// <summary>
        /// Substitui grupos de sensores por nucleo por um agregado.
        ///
        /// Um processador de 6 nucleos publica clock, tensao, potencia e uso de
        /// cada um: dezenas de entradas que dizem quase a mesma coisa e enterram
        /// os sensores gerais. O agregado mostra a media do grupo.
        /// </summary>
        private static List<SensorEntry> Condense(List<SensorEntry> raw)
        {
            Dictionary<string, List<SensorEntry>> groups = new Dictionary<string, List<SensorEntry>>();
            List<SensorEntry> keep = new List<SensorEntry>();

            foreach (SensorEntry e in raw)
            {
                if (!IsPerCore(e.Name)) { keep.Add(e); continue; }
                string key = GroupKey(e);
                if (!groups.ContainsKey(key)) groups[key] = new List<SensorEntry>();
                groups[key].Add(e);
            }

            foreach (KeyValuePair<string, List<SensorEntry>> kv in groups)
            {
                List<SensorEntry> members = kv.Value;
                if (members.Count == 1) { keep.Add(members[0]); continue; }

                SensorEntry agg = new SensorEntry();
                agg.Id = SynthPrefix + kv.Key;
                agg.Hardware = members[0].Hardware;
                agg.Category = members[0].Category;
                agg.Type = members[0].Type;
                agg.Unit = members[0].Unit;
                agg.Source = members[0].Source;
                agg.Members = members.Count;
                agg.Name = Normalize(members[0].Name) + " · " + T.AverageOf(members.Count);
                agg.Label = Describe(agg.Hardware, agg.Name, agg.Type);
                agg.Value = Average(members);
                keep.Add(agg);
            }
            return keep;
        }

        private static float? Average(List<SensorEntry> members)
        {
            double sum = 0; int n = 0;
            foreach (SensorEntry m in members)
            {
                if (!m.Value.HasValue || float.IsNaN(m.Value.Value) || float.IsInfinity(m.Value.Value)) continue;
                sum += m.Value.Value; n++;
            }
            return n > 0 ? (float?)(sum / n) : null;
        }

        /// <summary>Mapeia cada agregado aos identificadores que ele resume.</summary>
        private void BuildSynthetics()
        {
            _synth.Clear();
            Dictionary<string, List<string>> groups = new Dictionary<string, List<string>>();

            foreach (SensorEntry e in Snap())
            {
                if (!IsPerCore(e.Name)) continue;
                string key = GroupKey(e);
                if (!groups.ContainsKey(key)) groups[key] = new List<string>();
                groups[key].Add(e.Id);
            }

            foreach (KeyValuePair<string, List<string>> kv in groups)
                if (kv.Value.Count > 1) _synth[SynthPrefix + kv.Key] = kv.Value;
        }

        // ---------------- leitura ----------------

        /// <summary>Instantaneo id -&gt; valor, incluindo os agregados.</summary>
        public Dictionary<string, float> Snapshot()
        {
            Dictionary<string, float> map = new Dictionary<string, float>();
            foreach (SensorEntry e in Snap())
            {
                if (!e.Value.HasValue || float.IsNaN(e.Value.Value) || float.IsInfinity(e.Value.Value)) continue;
                map[e.Id] = e.Value.Value;
            }

            foreach (KeyValuePair<string, List<string>> kv in _synth)
            {
                double sum = 0; int n = 0;
                foreach (string id in kv.Value)
                {
                    float v;
                    if (!map.TryGetValue(id, out v) || float.IsNaN(v)) continue;
                    sum += v; n++;
                }
                if (n > 0) map[kv.Key] = (float)(sum / n);
            }
            return map;
        }

        /// <summary>
        /// Le um sensor pelo identificador, devolvendo valor E tipo.
        ///
        /// O tipo importa: a conversao para Fahrenheit so pode ser aplicada a
        /// sensores de temperatura.
        /// </summary>
        public SensorEntry ReadEntry(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return null;

            if (identifier.StartsWith(SynthPrefix, StringComparison.Ordinal))
                return ReadSynthetic(identifier);

            foreach (SensorEntry e in Snap())
                if (e.Id == identifier) return e;
            return null;
        }

        private SensorEntry ReadSynthetic(string id)
        {
            List<string> members;
            if (!_synth.TryGetValue(id, out members))
            {
                BuildSynthetics();                       // a lista pode nao ter sido montada ainda
                if (!_synth.TryGetValue(id, out members)) return null;
            }

            double sum = 0; int n = 0;
            SensorType type = SensorType.Clock;
            string hardware = "";
            string category = "Outros";
            string source = "LibreHardwareMonitor";
            string unit = null;

            foreach (SensorEntry e in Snap())
            {
                if (!members.Contains(e.Id)) continue;
                type = e.Type; hardware = e.Hardware; source = e.Source; category = e.Category; unit = e.Unit;
                if (!e.Value.HasValue || float.IsNaN(e.Value.Value)) continue;
                sum += e.Value.Value; n++;
            }

            SensorEntry agg = new SensorEntry();
            agg.Id = id;
            agg.Hardware = hardware;
            agg.Category = category;
            agg.Type = type;
            agg.Unit = unit;
            agg.Source = source;
            agg.Members = members.Count;
            agg.Name = T.AverageOf(members.Count);
            agg.Value = n > 0 ? (float?)(sum / n) : null;
            return agg;
        }

        public static string UnitOf(SensorType t)
        {
            switch (t)
            {
                case SensorType.Temperature: return ", C";
                case SensorType.Load: return ", %";
                case SensorType.Level: return ", %";
                case SensorType.Power: return ", W";
                case SensorType.Clock: return ", MHz";
                case SensorType.Voltage: return ", V";
                case SensorType.Current: return ", A";
                case SensorType.Fan: return ", RPM";
                case SensorType.Data: return ", GB";
                case SensorType.Throughput: return ", B/s";
                default: return "";
            }
        }

        public void Dispose()
        {
            SafeCloseComputer();
            if (_hw != null) { try { _hw.Dispose(); } catch { } _hw = null; }
        }
    }
}
