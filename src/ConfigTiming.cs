using System;

namespace MhiagosControl
{
    internal static class ConfigTiming
    {
        internal const int Maximum = 999;

        private static int Idle(int minutes) { return Math.Max(0, Math.Min(Maximum, minutes)); }
        private static int Rotation(int seconds) { return seconds <= 0 ? 0 : Math.Max(2, Math.Min(Maximum, seconds)); }

        internal static void Validate(Config config)
        {
            if (config.IdleBlankMinutes != Idle(config.IdleBlankMinutes) ||
                config.RotateSeconds != Rotation(config.RotateSeconds)) throw new FormatException(T.InvalidTiming);
        }

        internal static void NormalizeLegacy(Config config)
        {
            int idle = Idle(config.IdleBlankMinutes), rotation = Rotation(config.RotateSeconds);
            if (idle != config.IdleBlankMinutes || rotation != config.RotateSeconds)
                Log.Write("intervalos fora da faixa na configuracao local: inatividade " + config.IdleBlankMinutes +
                    " -> " + idle + " min; rodizio " + config.RotateSeconds + " -> " + rotation + " s");
            // Compatibilidade com arquivos locais antigos: limita em memoria e
            // registra o ajuste, sem sobrescrever o arquivo durante a leitura.
            config.IdleBlankMinutes = idle; config.RotateSeconds = rotation;
        }

        internal static int IdleMilliseconds(int minutes) { return Idle(minutes) * 60000; }
        internal static int RotationMilliseconds(int seconds) { return Rotation(seconds) * 1000; }
    }
}
