using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MhiagosControl
{
    /// <summary>Arquivo completo, versionado e verificado antes de restaurar.</summary>
    internal static class ConfigurationBackup
    {
        private const string Header = "MHIAGOS-CONFIG-BACKUP/1";
        private const int MaxBytes = 4 * 1024 * 1024;

        private static string Hash(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(bytes));
        }

        internal static void Write(Config config, string path)
        {
            ConfigTiming.Validate(config);
            byte[] data = Encoding.UTF8.GetBytes(config.Clone().ToText());
            string text = Header + "\n" + Hash(data) + "\n" + Convert.ToBase64String(data);
            if (text.Length > MaxBytes) throw new IOException(T.BackupTooLarge);
            // Exportar nunca deve substituir o arquivo de configuracao ativo.
            if (!string.IsNullOrEmpty(config.SavePath) && string.Equals(Path.GetFullPath(path),
                config.SavePath, StringComparison.OrdinalIgnoreCase)) throw new IOException(T.BackupOwnFile);
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, text, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        internal static Config Read(string path)
        {
            string text;
            using (FileStream stream = File.OpenRead(path))
            {
                if (stream.Length > MaxBytes) throw new FormatException(T.BackupTooLarge);
                using (StreamReader reader = new StreamReader(stream, new UTF8Encoding(false, true)))
                    text = reader.ReadToEnd();
            }
            string[] parts = text.Replace("\r", "").TrimEnd('\n').Split('\n');
            if (parts.Length != 3 || parts[0] != Header) throw new FormatException(T.BackupInvalid);
            byte[] data;
            try { data = Convert.FromBase64String(parts[2]); }
            catch (FormatException) { throw new FormatException(T.BackupInvalid); }
            if (Hash(data) != parts[1]) throw new FormatException(T.BackupInvalid);
            Config result = Config.FromText(new UTF8Encoding(false, true).GetString(data));
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Profile p in result.Profiles)
                if (string.IsNullOrWhiteSpace(p.Name) || !names.Add(p.Name))
                    throw new FormatException(T.BackupInvalid);
            if (!result.NameExists(result.ActiveName)) throw new FormatException(T.BackupInvalid);
            foreach (string profile in result.GameProfileNames)
                if (!result.NameExists(profile)) throw new FormatException(T.BackupInvalid);
            return result;
        }

        internal static int MissingSensors(Config config, IList<SensorEntry> sensors)
        {
            HashSet<string> available = new HashSet<string>(StringComparer.Ordinal);
            foreach (SensorEntry s in sensors) available.Add(s.Id);
            HashSet<string> missing = new HashSet<string>(StringComparer.Ordinal);
            foreach (Profile p in config.Profiles)
                foreach (string id in new string[] { p.Panel1Id, p.Panel2Id })
                    if (!string.IsNullOrEmpty(id) && !available.Contains(id)) missing.Add(id);
            foreach (string id in config.MetricIds)
                if (!available.Contains(id)) missing.Add(id);
            foreach (MetricProfile p in config.MetricProfiles)
                foreach (string id in p.Ids)
                    if (!available.Contains(id)) missing.Add(id);
            return missing.Count;
        }
    }
}
