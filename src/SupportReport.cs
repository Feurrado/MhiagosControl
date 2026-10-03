using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MhiagosControl
{
    internal static class SupportReport
    {
        // Lista permitida de campos: nao exporta nomes/IDs de sensores, jogos,
        // caminhos, nomes de perfis, usuario, numero de serie nem log bruto.
        internal static string Build(Config saved, IList<SensorEntry> sensors,
            IDictionary<string, float> snapshot, PanelStatus status, bool rtss, DateTime utcNow)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("Mhiagos Control - diagnostic / format 1");
            text.AppendLine("UTC: " + utcNow.ToString("O", CultureInfo.InvariantCulture));
            text.AppendLine("Windows: " + Environment.OSVersion.Version);
            text.AppendLine("CLR: " + Environment.Version);
            text.AppendLine("Process: " + (Environment.Is64BitProcess ? "64-bit" : "32-bit"));
            text.AppendLine("RTSS available: " + rtss);
            text.AppendLine("Panel connected: " + (status == null ? "unknown" : status.Connected.ToString()));
            text.AppendLine("Panel paused: " + (status == null ? "unknown" : status.Paused.ToString()));
            text.AppendLine("Profiles: " + saved.Profiles.Count);
            text.AppendLine("Game bindings: " + saved.GameKeys.Count);
            text.AppendLine("Game automation: " + saved.GameProfiles);
            text.AppendLine("Rotation seconds: " + saved.RotateSeconds);
            text.AppendLine("Idle blank minutes: " + saved.IdleBlankMinutes);
            text.AppendLine("Metric cards: " + saved.MetricIds.Count);
            text.AppendLine("Missing configured sensors: " + ConfigurationBackup.MissingSensors(saved, sensors));
            text.AppendLine();
            text.AppendLine("Sources (catalog / valid readings):");
            SortedDictionary<string, int[]> sources = new SortedDictionary<string, int[]>();
            foreach (SensorEntry sensor in sensors)
            {
                string source = sensor.Source == "HWiNFO" || sensor.Source == "LibreHardwareMonitor" || sensor.Source == "RTSS"
                    ? sensor.Source : "Other";
                if (!sources.ContainsKey(source)) sources.Add(source, new int[2]);
                sources[source][0]++;
                float value;
                if (snapshot != null && snapshot.TryGetValue(sensor.Id, out value) &&
                    !float.IsNaN(value) && !float.IsInfinity(value)) sources[source][1]++;
            }
            foreach (KeyValuePair<string, int[]> source in sources)
                text.AppendLine(source.Key + ": " + source.Value[0] + " / " + source.Value[1]);
            text.AppendLine();
            text.AppendLine("Privacy: no paths, names, serials, game executables or raw logs included.");
            text.AppendLine("Snapshot only. Valid readings do not prove hardware accuracy or freshness.");
            return text.ToString();
        }
    }
}
