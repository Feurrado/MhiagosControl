using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace MhiagosControl
{
    /// <summary>Identidade humana e visual de um processo publicado pelo RTSS.</summary>
    internal sealed class GameIdentityInfo
    {
        public string Key;
        public string DisplayName;
        public string ExecutablePath;
        public Image Icon;
    }

    internal static class GameIdentity
    {
        private sealed class InstalledGame
        {
            public string DisplayName;
            public string IconPath;
        }

        private static readonly object _lock = new object();
        private static readonly Dictionary<string, Image> _icons =
            new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Resolve nome e icone pelo processo real. ProductName/FileDescription
        /// ganham do nome do arquivo; o titulo da janela cobre jogos que nao
        /// preenchem a versao do executavel. O ultimo recurso humaniza a chave,
        /// mas nunca apresenta "algumjogo.exe" como nome principal.
        /// </summary>
        public static GameIdentityInfo Resolve(string key, int pid,
                                               string knownName, string knownPath)
        {
            GameIdentityInfo info = new GameIdentityInfo();
            info.Key = key ?? "";

            string path = File.Exists(knownPath) ? knownPath : null;
            string windowTitle = null;
            Process process = null;
            try
            {
                if (pid > 0) process = Process.GetProcessById(pid);
                else
                {
                    string baseName = Path.GetFileNameWithoutExtension(key ?? "");
                    if (!string.IsNullOrEmpty(baseName))
                    {
                        Process[] candidates = Process.GetProcessesByName(baseName);
                        if (candidates.Length > 0) process = candidates[0];
                        for (int i = 0; i < candidates.Length; i++)
                            if (candidates[i].MainWindowHandle != IntPtr.Zero)
                            { process = candidates[i]; break; }
                    }
                }

                if (process != null)
                {
                    try { windowTitle = process.MainWindowTitle; } catch { }
                    try
                    {
                        string current = process.MainModule.FileName;
                        if (File.Exists(current)) path = current;
                    }
                    catch { }
                }
            }
            catch { }
            finally { if (process != null) process.Dispose(); }

            string product = null, description = null;
            if (!string.IsNullOrEmpty(path))
                try
                {
                    FileVersionInfo version = FileVersionInfo.GetVersionInfo(path);
                    product = version.ProductName;
                    description = version.FileDescription;
                }
                catch { }

            // Jogos com anti-cheat frequentemente negam Process.MainModule.
            // O cadastro de desinstalacao do Windows ainda publica o nome e o
            // icone oficiais (VALORANT e League of Legends fazem isso).
            InstalledGame installed = null;
            if (string.IsNullOrEmpty(path) || IconFor(path) == null)
                installed = FindInstalledGame(key, knownName);
            if (installed != null && File.Exists(installed.IconPath) &&
                (string.IsNullOrEmpty(path) || IconFor(path) == null))
                path = installed.IconPath;

            info.ExecutablePath = path ?? knownPath ?? "";
            // Um nome ja confirmado no momento do vinculo nao deve ser trocado
            // depois por metadado generico do launcher ("Steam Client", por
            // exemplo). Na primeira descoberta ele esta vazio e os metadados
            // reais continuam sendo a fonte principal.
            info.DisplayName = PrimeiroNomeValido(knownName, product, description,
                installed != null ? installed.DisplayName : null, windowTitle);
            if (string.IsNullOrEmpty(info.DisplayName)) info.DisplayName = Humanize(key);
            if (string.IsNullOrEmpty(info.DisplayName)) info.DisplayName = T.DetectedGame;
            info.Icon = IconFor(info.ExecutablePath);
            return info;
        }

        private static string PrimeiroNomeValido(params string[] values)
        {
            foreach (string raw in values)
            {
                string value = Limpar(raw);
                if (string.IsNullOrEmpty(value)) continue;
                if (value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(value, "Unity Player", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(value, "Unreal Engine", StringComparison.OrdinalIgnoreCase)) continue;
                return value;
            }
            return null;
        }

        private static string Limpar(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            value = value.Trim();
            int separators = value.IndexOf(" - ", StringComparison.Ordinal);
            if (separators > 2 && value.Length > 80) value = value.Substring(0, separators);
            return value;
        }

        internal static string Humanize(string executable)
        {
            string name = Path.GetFileNameWithoutExtension(executable ?? "");
            if (string.IsNullOrEmpty(name)) return null;
            name = name.Replace('_', ' ').Replace('-', ' ');
            name = Regex.Replace(name, "(?<=[a-z])(?=[A-Z])", " ");
            name = Regex.Replace(name, "(?<=[A-Za-z])(?=[0-9])", " ");
            name = Regex.Replace(name, "\\s+", " ").Trim();
            if (name.Length == 0) return null;
            return char.ToUpperInvariant(name[0]) + name.Substring(1);
        }

        /// <summary>Compara sem pontuacao para casar "jogo.exe" com o nome instalado.</summary>
        internal static bool InstalledNameMatches(string displayName, string key, string knownName)
        {
            string installed = NormalizeName(displayName);
            if (installed.Length == 0) return false;
            string[] candidates = { NormalizeName(knownName), NormalizeName(Humanize(key)),
                                    NormalizeName(Path.GetFileNameWithoutExtension(key ?? "")) };
            foreach (string candidate in candidates)
            {
                if (candidate.Length == 0) continue;
                if (installed == candidate) return true;
                if (Math.Min(installed.Length, candidate.Length) >= 5 &&
                    (installed.Contains(candidate) || candidate.Contains(installed))) return true;
            }
            return false;
        }

        private static string NormalizeName(string value)
        {
            return Regex.Replace(value ?? "", "[^A-Za-z0-9]", "").ToLowerInvariant();
        }

        private static InstalledGame FindInstalledGame(string key, string knownName)
        {
            RegistryView[] views = { RegistryView.Registry64, RegistryView.Registry32 };
            RegistryHive[] hives = { RegistryHive.LocalMachine, RegistryHive.CurrentUser };
            foreach (RegistryHive hive in hives)
                foreach (RegistryView view in views)
                    try
                    {
                        using (RegistryKey root = RegistryKey.OpenBaseKey(hive, view))
                        using (RegistryKey uninstall = root.OpenSubKey(
                            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                        {
                            if (uninstall == null) continue;
                            foreach (string subKeyName in uninstall.GetSubKeyNames())
                                using (RegistryKey entry = uninstall.OpenSubKey(subKeyName))
                                {
                                    if (entry == null) continue;
                                    string displayName = entry.GetValue("DisplayName") as string;
                                    if (!InstalledNameMatches(displayName, key, knownName)) continue;
                                    string iconPath = CleanIconPath(entry.GetValue("DisplayIcon") as string);
                                    if (!File.Exists(iconPath)) continue;
                                    return new InstalledGame
                                    {
                                        DisplayName = displayName,
                                        IconPath = iconPath
                                    };
                                }
                        }
                    }
                    catch { }
            return null;
        }

        private static string CleanIconPath(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            string path = Environment.ExpandEnvironmentVariables(raw.Trim());
            if (path.Length > 1 && path[0] == '"')
            {
                int quote = path.IndexOf('"', 1);
                if (quote > 1) path = path.Substring(1, quote - 1);
            }
            else
            {
                int comma = path.LastIndexOf(',');
                int index;
                if (comma > 1 && int.TryParse(path.Substring(comma + 1), out index))
                    path = path.Substring(0, comma);
            }
            return path.Trim().Trim('"').Replace('/', Path.DirectorySeparatorChar);
        }

        private static Image IconFor(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            lock (_lock)
            {
                Image cached;
                if (_icons.TryGetValue(path, out cached)) return cached;
                try
                {
                    using (Icon icon = string.Equals(Path.GetExtension(path), ".ico",
                        StringComparison.OrdinalIgnoreCase)
                        ? new Icon(path, 64, 64)
                        : Icon.ExtractAssociatedIcon(path))
                        cached = icon != null ? icon.ToBitmap() : null;
                }
                catch { cached = null; }
                _icons[path] = cached;
                return cached;
            }
        }
    }
}
