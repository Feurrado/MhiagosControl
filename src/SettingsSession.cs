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
        internal Config SavedConfiguration { get { return _live.Clone(); } }

        internal bool TryRestore(Config restored, out string safetyPath, out string error)
        {
            safetyPath = null; error = null;
            try
            {
                if (string.IsNullOrEmpty(_live.SavePath)) throw new InvalidOperationException(T.BackupOwnFile);
                safetyPath = _live.SavePath + ".before-restore-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")
                    + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".mhiagos-backup";
                ConfigurationBackup.Write(_live, safetyPath);
                Config candidate = _live.Clone();
                candidate.CopyFrom(restored); // preserva o destino local, nunca grava no backup aberto
                if (!candidate.Save(out error)) return false;
                _live.CopyFrom(candidate);
                Draft.CopyFrom(candidate);
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }
        public Profile ActiveProfile { get { return _live.Active.Clone(); } }

        public bool TrySaveAll(out string error)
        {
            return TrySaveAll(false, out error);
        }

        public bool TrySaveAll(bool activateDraft, out string error)
        {
            Config candidate = Draft.Clone();
            // Uma ativacao externa nao e uma edicao do rascunho. So um comando
            // explicito Aplicar/Salvar perfil pode substitui-la.
            if (!activateDraft && candidate.NameExists(_live.ActiveName))
                candidate.ActiveName = _live.ActiveName;
            if (!candidate.Save(out error)) return false;
            _live.CopyFrom(candidate);
            Draft.ActiveName = candidate.ActiveName;
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
