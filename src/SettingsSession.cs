using System;

namespace MhiagosControl
{
    /// <summary>
    /// Unidade de edicao da janela. O formulario trabalha num rascunho; apenas
    /// uma gravacao valida substitui a configuracao viva do aplicativo.
    /// </summary>
    internal sealed class SettingsSession
    {
        private readonly Config _live;

        public SettingsSession(Config live)
        {
            if (live == null) throw new ArgumentNullException("live");
            _live = live;
            Draft = live.Clone();
        }

        public Config Draft { get; private set; }

        public bool TrySaveAll(out string error)
        {
            if (!Draft.Save(out error)) return false;
            _live.CopyFrom(Draft);
            return true;
        }

        /// <summary>
        /// Persiste somente preferencias globais escolhidas pelo chamador. Isso
        /// impede que um resize ou a barra lateral confirmem perfis em edicao.
        /// </summary>
        public bool TrySavePreferences(Action<Config, Config> copy, out string error)
        {
            if (copy == null) throw new ArgumentNullException("copy");
            Config candidate = _live.Clone();
            copy(Draft, candidate);
            if (!candidate.Save(out error)) return false;
            _live.CopyFrom(candidate);
            return true;
        }
    }

    /// <summary>Ativacao persistente compartilhada pela janela, bandeja e jogos.</summary>
    internal static class ProfileActivation
    {
        public static bool TryActivate(Config config, string name, out string error)
        {
            error = null;
            if (config == null) { error = "configuracao ausente"; return false; }
            if (string.IsNullOrEmpty(name) || !config.NameExists(name))
            {
                error = "perfil inexistente: " + (name ?? "");
                return false;
            }

            string previous = config.ActiveName;
            config.ActiveName = name;
            if (config.Save(out error)) return true;
            config.ActiveName = previous;
            return false;
        }
    }
}
