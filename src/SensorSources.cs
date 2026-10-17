using System;
using System.Collections.Generic;
using LibreHardwareMonitor.Hardware;

namespace MhiagosControl
{
    internal sealed class LibreSensorSource : ISensorSource
    {
        private Computer _computer;
        private readonly UpdateVisitor _visitor = new UpdateVisitor();
        public string Name { get { return "LibreHardwareMonitor"; } }
        public bool Available { get { return _computer != null; } }
        public bool LastReadComplete { get { return true; } }

        public bool Open()
        {
            try { OpenComputer(true, true, true); return true; }
            catch
            {
                CloseComputer();
                try { OpenComputer(false, false, false); return true; }
                catch (Exception ex) { CloseComputer(); Log.Error(Name + ": abertura", ex); return false; }
            }
        }

        private void OpenComputer(bool motherboard, bool memory, bool storage)
        {
            Computer c = new Computer();
            c.IsCpuEnabled = true; c.IsGpuEnabled = true;
            c.IsMotherboardEnabled = motherboard; c.IsMemoryEnabled = memory;
            c.IsStorageEnabled = storage; c.IsControllerEnabled = false;
            c.Open();
            _computer = c;
        }

        /// <summary>
        /// Os sensores que o ciclo precisa (null: todos). Com a janela fechada so
        /// o hardware dono desses sensores e atualizado: a varredura completa
        /// (CPU, GPU, placa, memoria, discos) custava ~48 ms por segundo para
        /// atualizar valores que ninguem le.
        /// </summary>
        public ICollection<string> Foco;

        public void Refresh()
        {
            if (_computer == null) return;
            try
            {
                ICollection<string> foco = Foco;
                if (foco == null || foco.Count == 0) { _computer.Accept(_visitor); return; }
                foreach (IHardware hw in _computer.Hardware)
                    if (PrecisaDe(hw, foco)) hw.Accept(_visitor);
            }
            catch (Exception ex) { Log.Error(Name + ": atualizacao", ex); }
        }

        /// <summary>O hardware, ou um dos sub-hardwares dele, e dono de algum sensor do foco.</summary>
        internal static bool PrecisaDe(IHardware hw, ICollection<string> foco)
        {
            if (Dono(hw.Identifier.ToString(), foco)) return true;
            foreach (IHardware sub in hw.SubHardware)
                if (PrecisaDe(sub, foco)) return true;
            return false;
        }

        internal static bool Dono(string identificador, ICollection<string> foco)
        {
            string prefixo = identificador + "/";
            foreach (string id in foco)
                if (id != null && id.StartsWith(prefixo, StringComparison.Ordinal)) return true;
            return false;
        }

        public List<SensorEntry> Read(IEnumerable<string> focusedIds)
        {
            List<SensorEntry> result = new List<SensorEntry>();
            if (_computer == null) return result;
            foreach (IHardware hw in _computer.Hardware)
            {
                AddHardware(result, hw);
                foreach (IHardware sub in hw.SubHardware) AddHardware(result, sub);
            }
            return result;
        }

        private static void AddHardware(List<SensorEntry> result, IHardware hw)
        {
            foreach (ISensor s in hw.Sensors)
            {
                SensorEntry e = new SensorEntry();
                e.Id = s.Identifier.ToString(); e.Hardware = hw.Name;
                e.Category = Sensors.CategoryOf(hw.HardwareType);
                e.Name = s.Name; e.Type = s.SensorType; e.Value = s.Value;
                e.Unit = e.EffectiveUnit;
                e.Label = Sensors.Describe(hw.Name, s.Name, s.SensorType);
                result.Add(e);
            }
        }

        private void CloseComputer()
        {
            if (_computer != null) { try { _computer.Close(); } catch { } _computer = null; }
        }

        public void Dispose() { CloseComputer(); }
    }

    internal sealed class HwInfoSensorSource : ISensorSource
    {
        private HwInfo _source;
        private readonly Dictionary<string, string> _groupById = new Dictionary<string, string>();
        private bool _complete = true;
        public string Name { get { return "HWiNFO"; } }
        public bool Available { get { return _source != null && _source.IsOpen; } }
        public bool LastReadComplete { get { return _complete; } }

        public bool Open()
        {
            try
            {
                HwInfo source = new HwInfo();
                if (source.Open()) { _source = source; return true; }
                source.Dispose();
            }
            catch (Exception ex) { Log.Error(Name + ": abertura", ex); }
            _source = null;
            return false;
        }

        public void Refresh() { }

        public List<SensorEntry> Read(IEnumerable<string> focusedIds)
        {
            List<HwReading> readings = ReadFocused(focusedIds);
            List<SensorEntry> result = new List<SensorEntry>();
            if (readings == null) return result;
            foreach (HwReading r in readings)
            {
                SensorEntry e = new SensorEntry();
                e.Id = r.Id; e.Hardware = r.Group;
                e.Category = Sensors.CategoryOf(r.Category, r.Group);
                e.Name = r.Label; e.Type = r.Type; e.Value = (float)r.Value;
                e.Unit = Sensors.CleanUnit(r.Unit); e.Source = Name;
                Sensors.ToGigabytes(e);
                e.Label = Sensors.Describe(r.Group, r.Label, r.Type);
                result.Add(e);
            }
            Sensors.CorrigirMemoriaDeVideo(result);
            return result;
        }

        private List<HwReading> ReadFocused(IEnumerable<string> focusedIds)
        {
            if (!Available) { _complete = true; return new List<HwReading>(); }
            if (focusedIds != null)
            {
                List<string> groups = new List<string>();
                foreach (string id in focusedIds)
                {
                    if (string.IsNullOrEmpty(id) || !id.StartsWith("hw:", StringComparison.Ordinal)) continue;
                    string group;
                    if (!_groupById.TryGetValue(id, out group)) { groups = null; break; }
                    if (!groups.Contains(group)) groups.Add(group);
                }
                if (groups != null)
                {
                    List<HwReading> focused = _source.ReadGroups(groups);
                    if (focused != null) { _complete = false; return focused; }
                }
            }

            List<HwReading> all = _source.ReadAll();
            _complete = true;
            _groupById.Clear();
            foreach (HwReading r in all) _groupById[r.Id] = r.Group;
            return all;
        }

        public void Dispose()
        {
            if (_source != null) { try { _source.Dispose(); } catch { } _source = null; }
        }
    }

    internal sealed class RtssSensorSource : ISensorSource
    {
        private readonly Rtss _source = new Rtss();
        public string Name { get { return "RTSS"; } }
        public bool Available { get { return true; } }
        public bool LastReadComplete { get { return true; } }
        public bool Open() { return true; }
        public void Refresh() { }
        public List<SensorEntry> Read(IEnumerable<string> focusedIds) { return _source.Ler(); }
        public void Dispose() { }
    }

    /// <summary>
    /// A bateria dos perifericos como sensores comuns: entram no mostrador do
    /// cooler, nos graficos de Metricas e nos perfis como qualquer temperatura.
    ///
    /// So repassa o que Perifericos ja leu - nao abre aparelho nenhum. Os que
    /// o historico conhece continuam na lista desligados, com leitura vazia:
    /// sumir com o headset desligado faria o perfil que o usa perder o sensor
    /// escolhido e o grafico perder a serie.
    /// </summary>
    internal sealed class BateriaSensorSource : ISensorSource
    {
        public const string Prefixo = "bat:";

        public string Name { get { return "Periféricos"; } }
        public bool Available { get { return true; } }
        public bool LastReadComplete { get { return true; } }
        public bool Open() { return true; }
        public void Refresh() { }
        public void Dispose() { }

        public static string IdDe(Periferico p)
        {
            return Prefixo + p.Fonte.ToString().ToLowerInvariant() + ":" + p.Nome;
        }

        public List<SensorEntry> Read(IEnumerable<string> focusedIds)
        {
            Dictionary<string, SensorEntry> porId = new Dictionary<string, SensorEntry>();
            List<SensorEntry> saida = new List<SensorEntry>();
            foreach (Periferico p in Perifericos.Atual)
            {
                // o campo Bluetooth vazio e aparelho sem leitura nao sao bateria
                if (p == null || (!p.Bateria.HasValue && p.Estado != EstadoPeriferico.Desligado)) continue;
                Add(saida, porId, p);
            }
            try
            {
                foreach (Periferico p in HistoricoDeBateria.Conhecidos()) Add(saida, porId, p);
            }
            catch (Exception ex) { Log.Error("aparelhos conhecidos para sensores", ex); }
            return saida;
        }

        private static void Add(List<SensorEntry> saida, Dictionary<string, SensorEntry> porId, Periferico p)
        {
            string id = IdDe(p);
            if (porId.ContainsKey(id)) return;
            SensorEntry e = new SensorEntry();
            e.Id = id;
            e.Hardware = p.Nome;
            e.Category = Sensors.CategoriaPerifericos;
            e.Name = T.MetricBattery(p.Nome);
            e.Type = SensorType.Level;
            e.Unit = "%";
            e.Value = p.Bateria.HasValue ? (float?)p.Bateria.Value : null;
            e.Source = "Periféricos";
            e.Label = e.Name;
            porId[id] = e;
            saida.Add(e);
        }
    }
}
