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

        public void Refresh()
        {
            if (_computer == null) return;
            try { _computer.Accept(_visitor); }
            catch (Exception ex) { Log.Error(Name + ": atualizacao", ex); }
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
}
