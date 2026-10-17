namespace MhiagosControl
{
    public static partial class T
    {
        public static string DataTools { get { return P("Backup e diagnóstico", "Backup and diagnostics"); } }
        public static string BackupTitle { get { return P("Backup completo", "Full backup"); } }
        public static string BackupNote { get { return P("Guarda perfis, jogos, métricas e preferências já salvos. Histórico e início automático do Windows ficam fora.", "Includes saved profiles, games, metrics and preferences. History and Windows startup registration are not included."); } }
        public static string BackupCreate { get { return P("Criar backup…", "Create backup…"); } }
        public static string BackupRestore { get { return P("Restaurar backup…", "Restore backup…"); } }
        public static string BackupInvalid { get { return P("Backup inválido, incompleto ou de uma versão não suportada. A configuração atual foi preservada.", "Invalid, incomplete or unsupported backup. Current settings were preserved."); } }
        public static string BackupTooLarge { get { return P("O backup excede o limite de 4 MB.", "The backup exceeds the 4 MB limit."); } }
        public static string BackupOwnFile { get { return P("Escolha um arquivo de backup separado da configuração em uso.", "Choose a backup file separate from the active configuration."); } }
        public static string RestorePreview { get { return P("Revisar restauração", "Review restore"); } }
        public static string RestoreApply { get { return P("Restaurar", "Restore"); } }
        public static string RestoreNote { get { return P("A restauração substitui as configurações atuais. Uma cópia do estado salvo atual será guardada na pasta de dados antes de aplicar. A janela será reaberta. O histórico e o início automático do Windows não serão alterados.", "Restoring replaces current settings. A copy of the current saved state will be kept in the data folder before applying. The window will reopen. History and Windows startup registration will remain unchanged."); } }
        public static string DiagnosticTitle { get { return P("Diagnóstico para suporte", "Support diagnostics"); } }
        public static string DiagnosticNote { get { return P("Revise o estado das fontes, do painel e da configuração antes de exportar. Não inclui nomes, caminhos, números de série ou logs pessoais.", "Review source, panel and settings status before exporting. Names, paths, serial numbers and personal logs are excluded."); } }
        public static string DiagnosticOpen { get { return P("Revisar diagnóstico…", "Review diagnostics…"); } }
        public static string SaveFile { get { return P("Salvar arquivo…", "Save file…"); } }
        public static string FileSaved { get { return P("Arquivo salvo.", "File saved."); } }
        public static string FileFailed { get { return P("Não foi possível concluir: ", "Could not complete: "); } }
        public static string GameBrowse { get { return P("Adicionar executável…", "Add executable…"); } }
        public static string GameFileNote { get { return P("Escolha o executável do jogo, não o launcher. O vínculo usa o nome do arquivo e o perfil selecionado; vale na hora. A detecção continua dependendo do RTSS.", "Choose the game executable, not its launcher. Binding uses the file name and selected profile and applies immediately. Detection still requires RTSS."); } }
        public static string GameReplace { get { return P("Este executável já possui um vínculo. Substituir pelo perfil selecionado?", "This executable is already bound. Replace it with the selected profile?"); } }
        public static string InvalidGameFile { get { return P("Selecione um arquivo .exe existente.", "Select an existing .exe file."); } }
        public static string Undo { get { return P("Desfazer", "Undo"); } }
        public static string ProfileDeleted(string name) { return P("Perfil apagado: " + name, "Profile deleted: " + name); }
        public static string MetricProfileDeleted(string name) { return P("Perfil de métricas apagado: " + name, "Metric profile deleted: " + name); }
        public static string ClickToRetry { get { return P("Clique aqui para tentar de novo.", "Click here to try again."); } }
        public static string TryAgain { get { return P("Tentar de novo", "Try again"); } }
        public static string CopyDetails { get { return P("Copiar detalhes", "Copy details"); } }
        public static string DetailsCopied { get { return P("Detalhes copiados", "Details copied"); } }
        public static string Close2 { get { return P("Fechar", "Close"); } }
        public static string UnexpectedErrorTitle { get { return P("Algo deu errado", "Something went wrong"); } }
        public static string UnexpectedErrorBody { get { return P("O Mhiagos Control encontrou um problema inesperado. Se o erro se repetir, copie os detalhes e envie junto com o pedido de ajuda.", "Mhiagos Control hit an unexpected problem. If it keeps happening, copy the details and include them when asking for help."); } }
        public static string Undone { get { return P("Desfeito", "Undone"); } }
        public static string KeyboardHelp { get { return P("Atalhos: Mudanças valem na hora · Alt+1…7 navega · Ctrl+B recolhe a barra", "Shortcuts: Changes apply immediately · Alt+1…7 navigates · Ctrl+B collapses sidebar"); } }
    }
}
