using System;
using System.IO;
using System.Text;

namespace MhiagosControl
{
    /// <summary>
    /// Registro em arquivo com rotacao simples.
    ///
    /// Um aplicativo que roda semanas em segundo plano nao pode falhar em
    /// silencio: sem isso, um erro intermitente as 3 da manha e indistinguivel
    /// de "parou de funcionar sozinho".
    /// </summary>
    public static class Log
    {
        private const long MaxBytes = 512 * 1024;
        private static readonly object _lock = new object();
        private static string _path;

        public static string Path { get { return _path; } }

        static Log()
        {
            try
            {
                _path = System.IO.Path.Combine(Paths.DataDir, "log.txt");
            }
            catch { _path = null; }
        }

        public static void Write(string message)
        {
            Emit("INFO ", message);
        }

        public static void Error(string context, Exception ex)
        {
            string detail = context;
            if (ex != null)
            {
                detail += " :: " + ex.GetType().Name + ": " + ex.Message;
                if (ex.InnerException != null)
                    detail += " <- " + ex.InnerException.Message;

                // Sem as primeiras linhas da pilha, uma NullReferenceException
                // diz apenas que algo era nulo - nao onde. Registrar o topo da
                // pilha transforma o log em diagnostico de verdade.
                detail += Environment.NewLine + Frames(ex, 6);
            }
            Emit("ERRO ", detail);
        }

        private static string Frames(Exception ex, int max)
        {
            try
            {
                Exception deepest = ex;
                while (deepest.InnerException != null) deepest = deepest.InnerException;
                if (string.IsNullOrEmpty(deepest.StackTrace)) return "        (sem pilha)";

                string[] lines = deepest.StackTrace.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < lines.Length && i < max; i++)
                {
                    if (i > 0) sb.AppendLine();
                    sb.Append("        " + lines[i].Trim());
                }
                return sb.ToString();
            }
            catch { return "        (pilha indisponivel)"; }
        }

        private static void Emit(string level, string message)
        {
            if (_path == null) return;
            lock (_lock)
            {
                try
                {
                    Rotate();
                    string line = string.Format("{0:yyyy-MM-dd HH:mm:ss} {1} {2}{3}",
                        DateTime.Now, level, message, Environment.NewLine);
                    File.AppendAllText(_path, line, Encoding.UTF8);
                }
                catch { /* registro nunca deve derrubar o aplicativo */ }
            }
        }

        private static void Rotate()
        {
            try
            {
                FileInfo fi = new FileInfo(_path);
                if (!fi.Exists || fi.Length < MaxBytes) return;
                string old = _path + ".1";
                if (File.Exists(old)) File.Delete(old);
                File.Move(_path, old);
            }
            catch { }
        }
    }

    /// <summary>Localizacao dos dados do aplicativo.</summary>
    public static class Paths
    {
        /// <summary>
        /// %LOCALAPPDATA%\MhiagosControl
        ///
        /// Escrever no diretorio do executavel funciona por acidente quando ele
        /// esta numa pasta gravavel, e quebra em Program Files. Dados de um
        /// unico usuario, em uma unica maquina, pertencem ao LocalApplicationData.
        /// </summary>
        private static bool _migrated = false;

        public static string DataDir
        {
            get
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string dir = System.IO.Path.Combine(local, "MhiagosControl");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                // o aplicativo se chamava RiseModePanel; traz perfis e log antigos
                if (!_migrated)
                {
                    _migrated = true;
                    try
                    {
                        string old = System.IO.Path.Combine(local, "RiseModePanel");
                        if (Directory.Exists(old))
                        {
                            foreach (string f in Directory.GetFiles(old))
                            {
                                string target = System.IO.Path.Combine(dir, System.IO.Path.GetFileName(f));
                                if (!File.Exists(target)) File.Copy(f, target);
                            }
                            Directory.Delete(old, true);
                        }
                    }
                    catch { /* migracao e conveniencia, nao pode impedir o arranque */ }
                }
                return dir;
            }
        }

        /// <summary>Pasta onde o executavel esta (usada so para migracao).</summary>
        public static string ExeDir
        {
            get
            {
                return System.IO.Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);
            }
        }
    }
}
