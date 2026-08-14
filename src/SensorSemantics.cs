using System;
using System.Collections.Generic;
using LibreHardwareMonitor.Hardware;

namespace MhiagosControl
{
    /// <summary>
    /// Resolve nomes portaveis usados em recuperacoes e migracoes para o sensor
    /// concreto publicado nesta maquina. O INI normal continua guardando o ID
    /// concreto; o nome semantico existe apenas ate o primeiro arranque valido.
    /// </summary>
    internal static class SensorSemantics
    {
        public const string Prefix = "semantic:";

        public static bool ResolveConfiguration(Config config, List<SensorEntry> sensors)
        {
            if (config == null || sensors == null || sensors.Count == 0) return false;
            bool changed = false;

            foreach (Profile p in config.Profiles)
            {
                string id = ResolveId(sensors, p.Panel1Id);
                if (id != p.Panel1Id) { p.Panel1Id = id; changed = true; }
                id = ResolveId(sensors, p.Panel2Id);
                if (id != p.Panel2Id)
                {
                    p.Panel2Id = id;
                    SensorEntry second = ById(sensors, id);
                    if (second != null) p.Percent = second.Type != SensorType.Power;
                    changed = true;
                }
            }

            for (int i = 0; i < config.MetricIds.Count; i++)
            {
                string id = ResolveId(sensors, config.MetricIds[i]);
                if (id != config.MetricIds[i]) { config.MetricIds[i] = id; changed = true; }
            }
            foreach (MetricProfile profile in config.MetricProfiles)
                for (int i = 0; i < profile.Ids.Count; i++)
                {
                    string id = ResolveId(sensors, profile.Ids[i]);
                    if (id != profile.Ids[i]) { profile.Ids[i] = id; changed = true; }
                }
            return changed;
        }

        public static string ResolveId(List<SensorEntry> sensors, string id)
        {
            string key = Key(id);
            if (key == null) return id;
            SensorEntry found = Find(sensors, key);
            return found == null || string.IsNullOrEmpty(found.Id) ? id : found.Id;
        }

        private static string Key(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (id.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                return id.Substring(Prefix.Length).ToLowerInvariant();
            switch (id.ToLowerInvariant())
            {
                case "cpu-temp": case "cpu-power": case "cpu-load":
                case "gpu-temp": case "gpu-load": return id.ToLowerInvariant();
            }
            return null;
        }

        private static SensorEntry Find(List<SensorEntry> sensors, string key)
        {
            switch (key)
            {
                case "cpu-temp": return Best(sensors, "CPU", SensorType.Temperature, "tctl", "package", "core");
                case "cpu-power": return Best(sensors, "CPU", SensorType.Power, "ppt", "package", "core");
                case "cpu-load": return Best(sensors, "CPU", SensorType.Load, "total", "cpu core");
                case "cpu-clock": return Best(sensors, "CPU", SensorType.Clock, "core", "effective");
                case "gpu-temp": return Best(sensors, "GPU", SensorType.Temperature, "thermal diode", "gpu core", "gpu temperature", "hot spot");
                case "gpu-load": return Best(sensors, "GPU", SensorType.Load, "gpu utilization", "gpu core", "d3d 3d");
                case "gpu-clock": return Best(sensors, "GPU", SensorType.Clock, "gpu core", "core");
                case "gpu-fan-pwm": return Best(sensors, "GPU", SensorType.Control, "gpu fan", "fan")
                                            ?? Best(sensors, "GPU", SensorType.Load, "gpu fan", "fan pwm");
                case "memory-used": return BestAny(sensors, SensorType.Data, "physical memory used", "memory used");
                case "vrm-temp": return NamedAny(sensors, SensorType.Temperature, "vrm mos", "vrm");
                case "vram-used": return Best(sensors, "GPU", SensorType.SmallData,
                                                "gpu memory used", "gpu memory usage", "d3d dedicated memory used", "dedicated memory used", "memory used")
                                      ?? Best(sensors, "GPU", SensorType.Data,
                                              "gpu memory used", "gpu memory usage", "d3d dedicated memory used", "dedicated memory used", "memory used");
                case "gpu-3d": return Best(sensors, "GPU", SensorType.Load, "d3d 3d", "3d usage", "gpu d3d");
            }
            return null;
        }

        private static SensorEntry Best(List<SensorEntry> sensors, string category,
                                        SensorType type, params string[] names)
        {
            return BestCore(sensors, category, type, names);
        }

        private static SensorEntry BestAny(List<SensorEntry> sensors, SensorType type,
                                           params string[] names)
        {
            return BestCore(sensors, null, type, names);
        }

        private static SensorEntry NamedAny(List<SensorEntry> sensors, SensorType type,
                                            params string[] names)
        {
            foreach (string wanted in names)
                foreach (SensorEntry s in sensors)
                    if (Matches(s, null, type) && Contains(s.Name, wanted)) return s;
            return null;
        }

        private static SensorEntry BestCore(List<SensorEntry> sensors, string category,
                                            SensorType type, string[] names)
        {
            foreach (string wanted in names)
                foreach (SensorEntry s in sensors)
                    if (Matches(s, category, type) && Contains(s.Name, wanted)) return s;
            foreach (SensorEntry s in sensors)
                if (Matches(s, category, type)) return s;
            return null;
        }

        private static bool Matches(SensorEntry s, string category, SensorType type)
        {
            return s != null && s.Type == type &&
                   (category == null || string.Equals(s.Category, category, StringComparison.Ordinal));
        }

        private static bool Contains(string value, string part)
        {
            return !string.IsNullOrEmpty(value) &&
                   value.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static SensorEntry ById(List<SensorEntry> sensors, string id)
        {
            foreach (SensorEntry s in sensors) if (s != null && s.Id == id) return s;
            return null;
        }
    }
}
