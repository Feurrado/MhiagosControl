using System;
using System.Globalization;

namespace MhiagosControl
{
    /// <summary>
    /// Textos da interface nos dois idiomas.
    ///
    /// Sem arquivo de recursos e sem satelite: o projeto compila com o csc.exe
    /// do Windows, sem SDK, e resx exigiria resgen no caminho do build. Uma
    /// propriedade por texto tem a vantagem que importa aqui - o compilador
    /// acusa o que faltou, o que um dicionario de chaves em string nao faz.
    ///
    /// Trocar de idioma reabre a janela de configuracao. Reetiquetar cada
    /// controle vivo exigiria que todos guardassem sua chave; reconstruir sai
    /// de graca e nao deixa canto por traduzir.
    /// </summary>
    public static partial class T
    {
        public const string PtBr = "pt-BR";
        public const string EnUs = "en-US";

        private static string _lang = PtBr;

        public static string Language
        {
            get { return _lang; }
            set { _lang = (value == EnUs) ? EnUs : PtBr; }
        }

        public static bool Pt { get { return _lang != EnUs; } }

        /// <summary>Idioma do Windows na primeira execucao, para nao comecar errado.</summary>
        public static string Detect()
        {
            try
            {
                string n = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                return string.Equals(n, "pt", StringComparison.OrdinalIgnoreCase) ? PtBr : EnUs;
            }
            catch { return PtBr; }
        }

        private static string P(string pt, string en) { return Pt ? pt : en; }

        // ---------------- geral ----------------

        public static string AppName { get { return "Mhiagos Control"; } }
        public static string Save { get { return P("Salvar", "Save"); } }
        public static string Close { get { return P("Fechar", "Close"); } }
        public static string Cancel { get { return P("Cancelar", "Cancel"); } }
        public static string Ok { get { return "OK"; } }
        public static string Apply { get { return P("Aplicar", "Apply"); } }
        public static string Change { get { return P("Trocar", "Change"); } }
        public static string Use { get { return P("Usar", "Use"); } }
        public static string NoReading { get { return P("sem leitura", "no reading"); } }
        public static string Off { get { return P("desligado", "off"); } }
        public static string On { get { return P("ligado", "on"); } }

        // ---------------- folha de especificacoes ----------------

        public static string NavSpecs { get { return P("Especificações", "Specs"); } }

        public static string SpecModel { get { return P("Modelo", "Model"); } }
        public static string SpecVendor { get { return P("Fabricante", "Vendor"); } }
        public static string SpecSpecification { get { return P("Especificação", "Specification"); } }
        public static string SpecFamily { get { return P("Família", "Family"); } }
        public static string SpecModelNum { get { return P("Modelo (nº)", "Model (num)"); } }
        public static string SpecStepping { get { return P("Stepping", "Stepping"); } }
        public static string SpecSocket { get { return P("Soquete", "Socket"); } }
        public static string SpecCores { get { return P("Núcleos", "Cores"); } }
        public static string SpecThreads { get { return P("Threads", "Threads"); } }
        public static string SpecMaxClock { get { return P("Clock máximo", "Max clock"); } }
        public static string SpecBusClock { get { return P("Barramento", "Bus clock"); } }
        public static string SpecL1 { get { return P("Cache L1", "L1 cache"); } }
        public static string SpecL2 { get { return P("Cache L2", "L2 cache"); } }
        public static string SpecL3 { get { return P("Cache L3", "L3 cache"); } }
        public static string SpecVirtualization { get { return P("Virtualização", "Virtualization"); } }

        public static string SpecRevision { get { return P("Revisão", "Revision"); } }
        public static string SpecBiosVendor { get { return P("BIOS", "BIOS"); } }
        public static string SpecBiosVersion { get { return P("Versão da BIOS", "BIOS version"); } }
        public static string SpecBiosDate { get { return P("Data da BIOS", "BIOS date"); } }

        public static string SpecTotal { get { return P("Total", "Total"); } }
        public static string SpecType { get { return P("Tipo", "Type"); } }
        public static string SpecMemSpeed { get { return P("Velocidade", "Speed"); } }
        public static string SpecChannels { get { return P("Canais", "Channels"); } }
        public static string SpecModules { get { return P("Pentes", "Modules"); } }

        public static string SpecChip { get { return P("Processador gráfico", "Graphics processor"); } }
        public static string SpecVram { get { return P("Memória de vídeo", "Video memory"); } }
        public static string SpecDeviceId { get { return P("ID do dispositivo", "Device ID"); } }
        public static string SpecVendorId { get { return P("ID do fabricante", "Vendor ID"); } }
        public static string SpecBoardMaker { get { return P("Fabricante da placa", "Board maker"); } }
        public static string SpecSlotEmpty(string nome) { return P(nome + ": vazio", nome + ": empty"); }
        public static string SpecDriver { get { return P("Driver", "Driver"); } }
        public static string SpecDriverDate { get { return P("Data do driver", "Driver date"); } }
        public static string SpecResolution { get { return P("Resolução", "Resolution"); } }
        public static string SpecVbios { get { return P("VBIOS", "VBIOS"); } }
        public static string SpecMonitor { get { return P("Monitor", "Display"); } }

        public static string SpecSlots { get { return P("Slots", "Slots"); } }
        public static string SpecMaxRam { get { return P("Máximo suportado", "Max supported"); } }
        public static string SpecSlotsUsed(int usados, int total)
        {
            return P(usados + " de " + total + " ocupados", usados + " of " + total + " used");
        }

        public static string SpecNetwork { get { return P("Rede", "Network"); } }
        public static string SpecLinkSpeed { get { return P("Velocidade do link", "Link speed"); } }

        public static string SpecStorage { get { return P("Armazenamento", "Storage"); } }
        public static string SpecUptime { get { return P("Ligado há", "Up for"); } }
        public static string SpecInstalled { get { return P("Instalado em", "Installed on"); } }
        public static string SpecSecureBoot { get { return P("Inicialização segura", "Secure Boot"); } }
        public static string SpecTpm { get { return P("TPM", "TPM"); } }

        /// <summary>"3 d 4 h" ou "4 h 12 min" - a maior unidade e a seguinte.</summary>
        public static string Duracao(TimeSpan t)
        {
            if (t.TotalDays >= 1)
                return P((int)t.TotalDays + " d " + t.Hours + " h",
                         (int)t.TotalDays + "d " + t.Hours + "h");
            if (t.TotalHours >= 1)
                return P(t.Hours + " h " + t.Minutes + " min",
                         t.Hours + "h " + t.Minutes + "m");
            return P(t.Minutes + " min", t.Minutes + "m");
        }
        public static string SpecVersion { get { return P("Versão", "Version"); } }
        public static string SpecArch { get { return P("Arquitetura", "Architecture"); } }

        public static string SpecsLoading
        {
            get { return P("Consultando o sistema…", "Querying the system…"); }
        }

        public static string SpecsCopy { get { return P("Copiar tudo", "Copy all"); } }
        public static string SpecsFailed { get { return P("Não foi possível consultar o sistema. Os detalhes estão no log.", "Couldn't query the system. Details are in the log."); } }
        public static string SpecsRetry { get { return P("Tentar de novo", "Try again"); } }
        public static string SpecsCopied { get { return P("Especificações copiadas", "Specs copied"); } }
        public static string SpecsOverview { get { return P("Resumo", "Overview"); } }
        public static string SpecSeeAll { get { return P("Ver tudo ›", "See all ›"); } }
        public static string SpecCoresTag(string n) { return P(n + " núcleos", n + " cores"); }
        public static string SpecThreadsTag(string n) { return P(n + " threads", n + " threads"); }
        public static string SpecChannelsTag(string n) { return n == "1" ? P("1 canal", "1 channel") : P(n + " canais", n + " channels"); }
        public static string SpecUpTo(string v) { return P("até " + v, "up to " + v); }
        public static string SpecDriverTag(string v) { return P("driver " + v, "driver " + v); }
        public static string SpecDisks(int n) { return n == 1 ? P("1 disco", "1 drive") : P(n + " discos", n + " drives"); }
        public static string SpecUptimeTag(string v) { return P("ligado há " + v, "up " + v); }

        public static string SpecsNote
        {
            get
            {
                return P("Coletado uma vez por sessão, porque a consulta ao Windows leva alguns segundos. Não inclui nome da máquina nem do usuário.",
                         "Collected once per session, because querying Windows takes a few seconds. Machine and user names are not included.");
            }
        }
        public static string Saved { get { return P("alterações salvas", "changes saved"); } }
        public static string SaveFailed(string erro)
        {
            return P("Não foi possível salvar: " + erro, "Could not save: " + erro);
        }

        // ---------------- barra lateral ----------------

        public static string NavOverview { get { return P("Visão geral", "Overview"); } }
        public static string NavPanels { get { return P("Painéis", "Panels"); } }
        public static string NavProfiles { get { return P("Perfis", "Profiles"); } }
        public static string NavMetrics { get { return P("Métricas", "Metrics"); } }
        public static string AddMetric { get { return P("Adicionar métrica", "Add metric"); } }
        public static string DefaultMetrics { get { return P("Conjuntos", "Presets"); } }
        public static string MetricProfiles { get { return P("Perfis de métricas", "Metric profiles"); } }
        public static string SaveMetricProfile { get { return P("Salvar perfil atual...", "Save current profile..."); } }
        public static string MetricProfileName { get { return P("Nome do perfil de métricas", "Metric profile name"); } }
        public static string UpdateMetricProfile { get { return P("Atualizar com a grade atual", "Update with current grid"); } }
        public static string RenameMetricProfile { get { return P("Renomear perfil...", "Rename profile..."); } }
        public static string DeleteMetricProfile { get { return P("Excluir perfil", "Delete profile"); } }
        public static string DefaultMetricProfileName(int index)
        {
            return P("Métricas " + index, "Metrics " + index);
        }
        public static string DeleteMetricProfileQ(string name)
        {
            return P("Excluir o perfil de métricas “" + name + "”?",
                     "Delete the metric profile “" + name + "”?");
        }
        public static string MetricProfileSaved(string name)
        {
            return P("Perfil de métricas salvo: " + name, "Metric profile saved: " + name);
        }
        public static string MetricProfileApplied(string name)
        {
            return P("Perfil de métricas aplicado: " + name, "Metric profile applied: " + name);
        }

        /// <summary>Titulos curtos que cabem nos cinco cartoes do painel inicial.</summary>
        public static string DashboardMetric(int index, string fallback)
        {
            switch (index)
            {
                case 0: return P("Temperatura da CPU", "CPU temperature");
                case 1: return P("Uso da CPU", "CPU usage");
                case 2: return P("Temperatura da GPU", "GPU temperature");
                case 3: return P("Uso da GPU", "GPU usage");
                case 4: return P("Uso da memória", "Memory usage");
                default: return fallback;
            }
        }

        /// <summary>
        /// Nome de cada conjunto pronto. Chave, e nao indice: a ordem da lista
        /// pode mudar, e um switch por posicao renomearia tudo em silencio.
        /// </summary>
        public static string NomeDoConjunto(string chave)
        {
            switch (chave)
            {
                case "auto":        return P("Automático (por peça)", "Automatic (per device)");
                case "jogos":       return P("Jogos", "Gaming");
                case "termico":     return P("Térmico", "Thermal");
                case "silencioso":  return P("Silencioso", "Acoustic");
            }
            return chave;
        }

        public static string PresetEmpty
        {
            get
            {
                return P("Este conjunto não encontrou leituras nesta máquina.",
                         "This preset found no readings on this machine.");
            }
        }

        public static string PresetApplied(string nome, int n)
        {
            return P(nome + ": " + n + " cartões", nome + ": " + n + " cards");
        }

        // Extremos e media da janela desenhada. Abreviados porque dividem uma
        // linha so, dentro de um cartao que pode ter 350 px.
        public static string StatMin { get { return P("mín", "min"); } }
        public static string StatAvg { get { return P("méd", "avg"); } }
        public static string StatMax { get { return P("máx", "max"); } }
        public static string MetricsHint
        {
            get
            {
                return P("Passe o ponteiro sobre um cartão para mover, redimensionar ou remover.",
                         "Hover a card to move, resize or remove it.");
            }
        }
        public static string NoMetrics
        {
            get
            {
                return P("Nenhuma leitura disponível — as fontes de sensores não responderam.",
                         "No readings available — the sensor sources did not respond.");
            }
        }
        public static string NavAbout { get { return P("Sobre", "About"); } }
        public static string ActiveProfile { get { return P("PERFIL ATIVO", "ACTIVE PROFILE"); } }

        // ---------------- tela de bordo ----------------

        public static string CoolerCard { get { return P("Cooler", "Cooler"); } }

        public static string SpecCpu { get { return P("Processador", "Processor"); } }
        public static string SpecGpu { get { return P("Vídeo", "Graphics"); } }
        public static string SpecRam { get { return P("Memória", "Memory"); } }
        public static string SpecBoard { get { return P("Placa-mãe", "Motherboard"); } }
        public static string SpecOs { get { return P("Sistema", "System"); } }

        public static string CoresAndThreads(int nucleos, int threads)
        {
            return P(nucleos + " núcleos  ·  " + threads + " threads",
                     nucleos + " cores  ·  " + threads + " threads");
        }

        public static string ThreadsOnly(int threads)
        {
            return P(threads + " threads", threads + " threads");
        }

        public static string OnPanel(string qual) { return P("Mostrador " + qual, "Display " + qual); }

        public static string Top { get { return P("de cima", "top"); } }
        public static string Bottom { get { return P("de baixo", "bottom"); } }

        public static string PanelLabel { get { return P("Painel", "Panel"); } }

        // ---------------- perfil por jogo ----------------

        public static string GameProfilesCard
        {
            get { return P("Perfil por jogo", "Profile per game"); }
        }

        public static string GameProfilesOn
        {
            get { return P("Trocar de perfil sozinho quando um jogo abrir",
                           "Switch profile automatically when a game starts"); }
        }

        public static string GameProfilesNote
        {
            get
            {
                return P("Depende do RivaTuner Statistics Server, que identifica o jogo em primeiro plano. Ao fechar o jogo, o perfil padrão volta.",
                         "Requires the RivaTuner Statistics Server, which identifies the foreground game. The default profile returns when the game closes.");
            }
        }

        public static string CurrentGame { get { return P("Jogo detectado agora", "Game detected now"); } }
        public static string GameBindings { get { return P("Vínculos", "Bindings"); } }
        public static string NoGameBindings
        {
            get { return P("Nenhum jogo foi vinculado. Abra um jogo e associe o perfil selecionado.",
                           "No game has been bound. Open a game and associate the selected profile."); }
        }
        public static string DetectedGame { get { return P("Jogo detectado", "Detected game"); } }
        public static string SelectedProfile(string name)
        {
            return P("Perfil selecionado: " + name, "Selected profile: " + name);
        }
        public static string BoundProfile(string name)
        {
            return P("Perfil vinculado: " + name, "Bound profile: " + name);
        }

        public static string BindGame { get { return P("Vincular jogo atual", "Bind current game"); } }
        public static string BindGameTo(string profile)
        {
            return P("Vincular a " + profile, "Bind to " + profile);
        }
        public static string UnbindGame { get { return P("Desvincular", "Unbind"); } }

        public static string NoGameToBind
        {
            get { return P("nenhum jogo detectado agora", "no game detected right now"); }
        }

        public static string GameBound(string jogo, string perfil)
        {
            return P(jogo + " → " + perfil, jogo + " → " + perfil);
        }

        public static string GamesBoundCount(int n)
        {
            if (n == 0) return P("nenhum jogo vinculado", "no games bound");
            if (n == 1) return P("1 jogo vinculado", "1 game bound");
            return P(n + " jogos vinculados", n + " games bound");
        }


        // ---------------- pagina: paineis ----------------

        // Os dois mostradores da peca ficam EMPILHADOS, nao lado a lado: o de
        // temperatura em cima. Conferido contra foto do aparelho ligado.
        public static string Panel1 { get { return P("Painel 1  ·  cima", "Panel 1  ·  top"); } }
        public static string Panel2 { get { return P("Painel 2  ·  baixo", "Panel 2  ·  bottom"); } }

        /// <summary>Sem a posicao, para linhas de resumo onde ela so ocupa espaco.</summary>
        public static string PanelShort(int n) { return P("Painel " + n, "Panel " + n); }
        public static string Scale { get { return P("Escala", "Scale"); } }
        public static string Unit { get { return P("Unidade", "Unit"); } }

        /// <summary>
        /// O firmware acende sempre um dos dois simbolos do par e nao tem
        /// estado apagado - varrido e confirmado, veja o README. Sem esta nota
        /// quem poe RPM ou MHz no mostrador procura o botao que desliga o "W".
        /// </summary>
        public static string UnitAlwaysOn
        {
            get
            {
                return P("O cooler acende sempre um dos dois — não há como apagar.",
                         "The cooler always lights one of the two — it cannot be blanked.");
            }
        }
        public static string Preview { get { return P("Prévia", "Preview"); } }
        public static string NoSensorChosen { get { return P("nenhum sensor escolhido", "no sensor chosen"); } }
        public static string PickSensor1 { get { return P("Sensor do painel 1", "Panel 1 sensor"); } }
        public static string PickSensor2 { get { return P("Sensor do painel 2", "Panel 2 sensor"); } }

        // ---------------- selecao de sensor ----------------

        public static string SearchSensor { get { return P("Buscar sensor...", "Search sensor..."); } }
        public static string CatAll { get { return P("Todos", "All"); } }

        /// <summary>
        /// Rotulo de exibicao de uma categoria. O valor guardado continua sendo
        /// o canonico em portugues: ele e chave de agrupamento, nao texto de
        /// tela, e traduzi-lo na origem quebraria a comparacao.
        /// </summary>
        public static string Category(string canonical)
        {
            if (Pt || string.IsNullOrEmpty(canonical)) return canonical;
            switch (canonical)
            {
                case "Jogos": return "Games";
                case "Placa-mãe": return "Motherboard";
                case "Memória": return "Memory";
                case "Disco": return "Disk";
                case "Rede": return "Network";
                case "Periféricos": return "Peripherals";
                case "Outros": return "Other";
                default: return canonical;      // CPU e GPU nao mudam
            }
        }

        public static string AverageOf(int n)
        {
            return P("média de " + n, "average of " + n);
        }

        // ---------------- quadros por segundo (RTSS) ----------------

        /// <summary>
        /// A janela entra no proprio rotulo.
        ///
        /// "FPS mínimo" sozinho nao diz mínimo de quando, e cada monitor de
        /// hardware usa uma janela diferente. Escrita ali, ninguem precisa
        /// adivinhar - nem confundir com o "1% low", que e outra conta e exige
        /// o tempo de cada quadro.
        /// </summary>
        private static string Janela { get { return " (" + Rtss.JanelaSeg + " s)"; } }

        public static string MetricFps { get { return P("Taxa de quadros", "Frame rate"); } }
        public static string MetricFpsMin { get { return P("FPS mínimo", "Min FPS") + Janela; } }
        public static string MetricFpsAvg { get { return P("FPS médio", "Avg FPS") + Janela; } }
        public static string MetricFrametime { get { return P("Tempo de quadro", "Frame time"); } }
        public static string MetricFrametimeAvg { get { return P("Tempo de quadro médio", "Avg frame time") + Janela; } }
        public static string MetricFrametimeMax { get { return P("Pior tempo de quadro", "Worst frame time") + Janela; } }

        public static string RtssMissing { get { return P("RTSS não encontrado", "RTSS not found"); } }
        public static string RtssIdle { get { return P("nenhum jogo em execução", "no game running"); } }


        public static string RtssActive
        {
            get
            {
                return P("RTSS ativo — as leituras de FPS estão disponíveis.",
                         "RTSS is running — FPS readings are available.");
            }
        }

        public static string RtssAbsent
        {
            get
            {
                return P("RTSS não encontrado — sem ele não há como medir quadros.",
                         "RTSS not found — without it there is no way to measure frames.");
            }
        }

        public static string RtssNote
        {
            get
            {
                return P("Quantos quadros um jogo desenha não está em sensor nenhum. Quem mede é o RivaTuner " +
                         "Statistics Server, gratuito e avulso. A instalação limpa já basta: não há nada para " +
                         "configurar depois, e as leituras aparecem sozinhas.",
                         "How many frames a game draws is in no sensor. What measures it is RivaTuner Statistics " +
                         "Server, free and standalone. A clean install is enough: there is nothing to configure " +
                         "afterwards, and the readings show up on their own.");
            }
        }

        public static string InstallRtss { get { return P("Instalar o RTSS", "Install RTSS"); } }
        public static string RtssPage { get { return P("Página de download", "Download page"); } }
        public static string Recheck { get { return P("Conferir de novo", "Check again"); } }

        public static string RtssInstalling
        {
            get
            {
                return P("Instalando pelo winget, numa janela à parte. Ao terminar, use Conferir de novo.",
                         "Installing through winget, in a separate window. When it finishes, use Check again.");
            }
        }

        public static string RtssConfigFailed(string erro)
        {
            return P("Não deu para ajustar o RTSS: " + erro,
                     "Could not adjust RTSS: " + erro);
        }

        public static string RtssNoWinget
        {
            get
            {
                return P("O winget não existe nesta máquina — use a página de download.",
                         "winget is not available on this machine — use the download page.");
            }
        }

        public static string NavSettings { get { return P("Configurações", "Settings"); } }

        // ---------------- pagina: perfis ----------------

        public static string SavedProfiles { get { return P("Perfis salvos", "Saved profiles"); } }
        public static string ProfilePreview { get { return P("Prévia do perfil selecionado", "Preview of the selected profile"); } }
        public static string New { get { return P("Novo", "New"); } }
        public static string Rename { get { return P("Renomear", "Rename"); } }
        public static string Duplicate { get { return P("Duplicar", "Duplicate"); } }
        public static string Delete { get { return P("Excluir", "Delete"); } }
        public static string Export { get { return P("Exportar", "Export"); } }
        public static string Import { get { return P("Importar", "Import"); } }
        public static string ExportProfile { get { return P("Exportar perfil", "Export profile"); } }
        public static string ImportProfile { get { return P("Importar perfil", "Import profile"); } }
        public static string ProfileFilter
        {
            get { return P("Perfil do Mhiagos Control (*.ini)|*.ini|Todos os arquivos|*.*",
                           "Mhiagos Control profile (*.ini)|*.ini|All files|*.*"); }
        }

        public static string Exported(string nome)
        {
            return P("Perfil exportado: " + nome, "Profile exported: " + nome);
        }

        public static string Imported(string nome)
        {
            return P("Perfil importado: " + nome, "Profile imported: " + nome);
        }

        public static string ExportFailed(string erro)
        {
            return P("Não deu para exportar: " + erro, "Could not export: " + erro);
        }

        public static string ImportFailed(string erro)
        {
            return P("Não deu para importar: " + erro, "Could not import: " + erro);
        }

        /// <summary>O identificador do sensor carrega o modelo do hardware; nao viaja entre maquinas.</summary>
        public static string ImportedUnknownSensor
        {
            get
            {
                return P("Perfil importado, mas os sensores dele não existem nesta máquina — escolha os dois de novo.",
                         "Profile imported, but its sensors do not exist on this machine — pick both again.");
            }
        }
        public static string ActiveBadge { get { return P("ATIVO", "ACTIVE"); } }
        public static string DefaultBadge { get { return P("PADRÃO", "DEFAULT"); } }
        public static string SetAsDefault { get { return P("Definir como padrão", "Set as default"); } }
        public static string AlreadyDefault { get { return P("Perfil padrão", "Default profile"); } }
        public static string DefaultProfileSet(string nome)
        {
            return P("Perfil padrão: " + nome, "Default profile: " + nome);
        }

        public static string Rotation { get { return P("Rodízio", "Rotation"); } }

        public static string IncludeInRotation
        {
            get { return P("Incluir o perfil selecionado no rodízio",
                           "Include the selected profile in the rotation"); }
        }

        public static string RotationOff
        {
            get
            {
                return P("marque dois ou mais perfis para o mostrador girar entre eles",
                         "mark two or more profiles for the display to cycle between them");
            }
        }

        public static string RotationOn(int n)
        {
            return P("segundos em cada perfil  ·  " + n + " perfis no rodízio",
                     "seconds on each profile  ·  " + n + " profiles in the rotation");
        }
        public static string ApplyProfile { get { return P("Aplicar perfil", "Apply profile"); } }
        public static string AlreadyActive { get { return P("Perfil ativo", "Active profile"); } }

        public static string ProfilesNote
        {
            get
            {
                return P(
                    "Aplicar envia o perfil ao mostrador. O padrão volta quando um jogo termina.\n" +
                    "Todos aparecem no menu da bandeja para troca rápida, sem abrir esta janela.",
                    "Apply sends the profile to the display. The default returns when a game ends.\n" +
                    "All profiles appear in the tray menu for quick switching without opening this window.");
            }
        }

        public static string NewProfileName { get { return P("Nome do novo perfil:", "Name of the new profile:"); } }
        public static string CopyName { get { return P("Nome da cópia:", "Name of the copy:"); } }
        public static string NewName { get { return P("Novo nome:", "New name:"); } }
        public static string DefaultProfileName(int n) { return P("Perfil " + n, "Profile " + n); }
        public static string CopySuffix { get { return P(" (cópia)", " (copy)"); } }
        public static string NameTaken { get { return P("Já existe um perfil com esse nome.", "A profile with that name already exists."); } }
        public static string KeepOneProfile { get { return P("É preciso manter ao menos um perfil.", "At least one profile must be kept."); } }
        public static string DeleteProfileQ(string name)
        {
            return P("Excluir o perfil \"" + name + "\"?", "Delete the profile \"" + name + "\"?");
        }

        // ---------------- pagina: sobre ----------------

        public static string AboutTagline
        {
            get
            {
                return P(
                    "Driver alternativo para o painel dos coolers Rise Mode Temp 6, Temp 6 Pro e Temp 8.\n" +
                    "Protocolo levantado por engenharia reversa; qualquer sensor pode ir para qualquer mostrador.",
                    "An alternative driver for the LED panel of the Rise Mode Temp 6, Temp 6 Pro and Temp 8 coolers.\n" +
                    "Protocol recovered by reverse engineering; any sensor can go to either display.");
            }
        }

        // Configuracoes em linhas, no estilo do Windows 11: grupos e descricoes curtas.
        public static string SettingsGeneral { get { return P("Geral", "General"); } }
        public static string SettingsFps { get { return P("FPS", "FPS"); } }
        public static string SettingsData { get { return P("Dados", "Data"); } }
        public static string StateOn { get { return P("Ligado", "On"); } }
        public static string StateOff { get { return P("Desligado", "Off"); } }
        public static string StartWithWindowsNote
        {
            get { return P("O app abre sozinho quando você entra no Windows.", "The app opens on its own when you sign in to Windows."); }
        }
        public static string ShowAllShort
        {
            get { return P("Desligado, os sensores por núcleo viram uma média por grupo.", "When off, per-core sensors become one average per group."); }
        }
        public static string BlankWhenIdleShort
        {
            get { return P("Conta teclado e mouse da sessão inteira. O mostrador volta ao primeiro toque.",
                           "Counts keyboard and mouse across the whole session. The display returns on the first input."); }
        }
        public static string DataFolderTitle { get { return P("Configuração e registro", "Settings and log"); } }
        public static string BackupShort
        {
            get { return P("Perfis, jogos, métricas e preferências já salvos. Edições pendentes e histórico ficam fora.",
                           "Saved profiles, games, metrics and preferences. Unsaved edits and history are left out."); }
        }
        public static string DiagnosticShort
        {
            get { return P("Revise antes de exportar. Sem nomes, caminhos, números de série ou logs pessoais.",
                           "Review before exporting. No names, paths, serial numbers or personal logs."); }
        }

        // Sobre com cartao de identidade
        public static string VersionLabel(string v) { return P("Versão " + v, "Version " + v); }
        public static string Libraries { get { return P("Bibliotecas", "Libraries"); } }
        public static string PanelNone { get { return P("nenhum encontrado", "none found"); } }
        public static string PanelTested { get { return P("Temp 6 Pro Black, o modelo testado", "Temp 6 Pro Black, the tested model"); } }
        public static string Language_ { get { return P("Idioma", "Language"); } }
        public static string LanguageNote
        {
            get
            {
                return P("A janela é reaberta para aplicar o idioma. Nada é perdido.",
                         "The window reopens to apply the language. Nothing is lost.");
            }
        }

        public static string StartWithWindows { get { return P("Iniciar junto com o Windows", "Start with Windows"); } }
        public static string ShowAllSensors { get { return P("Mostrar todos os sensores (inclui um por núcleo)", "Show every sensor (includes one per core)"); } }
        public static string OpenFolder { get { return P("Abrir pasta", "Open folder"); } }
        public static string CreatedBy { get { return P("Criado por Feurrado", "Built by Feurrado"); } }
        public static string OpenOnGitHub { get { return P("Abrir no GitHub", "Open on GitHub"); } }
        public static string LibsNote
        {
            get
            {
                return P(
                    "Código deste projeto sob licença MIT · sensores pela LibreHardwareMonitor (MPL 2.0)\n" +
                    "e pela biblioteca cliente do HWiNFO, © REALiX s.r.o.",
                    "This project's code is MIT licensed · sensors by LibreHardwareMonitor (MPL 2.0)\n" +
                    "and by the HWiNFO client library, © REALiX s.r.o.");
            }
        }
        public static string DisclaimerTitle { get { return P("Isenção de responsabilidade", "Disclaimer"); } }

        public static string Disclaimer
        {
            get
            {
                return P(
        "Este é um projeto pessoal, independente e sem fins lucrativos, feito para interoperar com\n" +
        "hardware que o autor possui. Não tem qualquer vínculo, patrocínio, afiliação ou aprovação\n" +
        "da Rise Mode, da Ocypus, da SHENZHEN SHINETEK, da REALiX s.r.o. ou de qualquer outro\n" +
        "fabricante. Todas as marcas citadas pertencem aos seus respectivos donos e aparecem apenas\n" +
        "para identificar o equipamento com que o programa se comunica.\n" +
        "\n" +
        "O protocolo do painel foi levantado por engenharia reversa do próprio equipamento, com a\n" +
        "finalidade exclusiva de interoperabilidade — o programa não contém, não copia e não\n" +
        "redistribui código do software original.\n" +
        "\n" +
        "O PROGRAMA É FORNECIDO \"COMO ESTÁ\", SEM GARANTIA DE QUALQUER TIPO, EXPRESSA OU IMPLÍCITA,\n" +
        "INCLUINDO AS DE COMERCIALIZAÇÃO, ADEQUAÇÃO A UM FIM ESPECÍFICO E NÃO VIOLAÇÃO. O USO É POR\n" +
        "CONTA E RISCO DE QUEM O EXECUTA. EM NENHUMA HIPÓTESE O AUTOR RESPONDE POR QUALQUER DANO,\n" +
        "DIRETO OU INDIRETO, INCLUINDO DANO A EQUIPAMENTO, PERDA DE DADOS OU LUCROS CESSANTES,\n" +
        "DECORRENTE DO USO OU DA IMPOSSIBILIDADE DE USO DESTE PROGRAMA.\n" +
        "\n" +
        "Usar este programa pode implicar a perda da garantia do equipamento. Verifique antes.",

        "This is a personal, independent, non-commercial project, built to interoperate with hardware the\n" +
        "author owns. It has no connection, sponsorship, affiliation or endorsement from Rise Mode, Ocypus,\n" +
        "SHENZHEN SHINETEK, REALiX s.r.o. or any other manufacturer. All trademarks mentioned belong to\n" +
        "their respective owners and appear only to identify the equipment the program talks to.\n" +
        "\n" +
        "The panel protocol was recovered by reverse engineering the device itself, for the sole purpose\n" +
        "of interoperability — the program contains no code from the original software, copies none and\n" +
        "redistributes none.\n" +
        "\n" +
        "THE SOFTWARE IS PROVIDED \"AS IS\", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING\n" +
        "THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. USE IS\n" +
        "AT THE RISK OF WHOEVER RUNS IT. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY DAMAGE, DIRECT\n" +
        "OR INDIRECT, INCLUDING DAMAGE TO EQUIPMENT, LOSS OF DATA OR LOST PROFITS, ARISING FROM THE USE\n" +
        "OR THE INABILITY TO USE THIS PROGRAM.\n" +
        "\n" +
        "Using this program may void the warranty of your equipment. Check first.");
            }
        }

        // ---------------- avisos e dialogos ----------------

        public static string UnsavedTitle { get { return P("Alterações não salvas", "Unsaved changes"); } }
        public static string UnsavedQuestion
        {
            get
            {
                return P("Há alterações que ainda não foram salvas.\n\nSalvar antes de fechar?",
                         "There are changes that have not been saved.\n\nSave before closing?");
            }
        }
        public static string AutostartFailed
        {
            get
            {
                return P("Não foi possível alterar a inicialização automática.\nDetalhes em:\n",
                         "Could not change the automatic startup.\nDetails in:\n");
            }
        }
        public static string AutostartUnsafeFolder
        {
            get
            {
                return P("A inicialização automática roda o aplicativo como administrador, e a pasta dele pode ser alterada por qualquer programa:\n\n{0}\n\nInstale pelo instalador (em Arquivos de Programas) para ativar esta opção.",
                         "Automatic startup runs the application as administrator, and its folder can be changed by any program:\n\n{0}\n\nInstall it with the installer (in Program Files) to enable this option.");
            }
        }

        // ---------------- motor de sensores ----------------

        public static string BlankWhenIdle
        {
            get { return P("Apagar o mostrador quando ninguém usar o computador",
                           "Blank the display when nobody is using the computer"); }
        }

        public static string BlankWhenIdleNote
        {
            get
            {
                return P("Conta teclado e mouse da sessão inteira. Assistir vídeo ou esperar uma renderização longa " +
                         "conta como ocioso — o mostrador volta ao primeiro toque.",
                         "Counts keyboard and mouse across the whole session. Watching a video or waiting on a long " +
                         "render counts as idle — the display returns on the first input.");
            }
        }

        /// <summary>Fica a direita do campo de minutos, entao le-se "15 minutos parado...".</summary>
        public static string MinutesIdle
        {
            get { return P("minutos parado até apagar", "minutes idle before blanking"); }
        }

        public static string SensorSources { get { return P("Fontes de sensores", "Sensor sources"); } }
        public static string SensorsFrom(string fonte, int n)
        {
            return P(fonte + ": " + n + " sensores", fonte + ": " + n + " sensors");
        }

        public static string EngineMissing
        {
            get
            {
                return P(
                    "Sem a biblioteca do HWiNFO em engine\\, temperatura, potência e clock real da CPU não são lidos.\n" +
                    "Reinstalar pelo instalador oficial repõe a pasta engine\\ completa.",
                    "Without the HWiNFO library in engine\\, CPU temperature, power and real clock are not read.\n" +
                    "Reinstalling with the official installer restores the full engine\\ folder.");
            }
        }

        public static string AdoptEngineTitle { get { return P("Biblioteca de sensores", "Sensor library"); } }

        public static string AdoptEngineQuestion(string origem, string destino)
        {
            return P(
                "A biblioteca que lê temperatura e potência da CPU não está nesta instalação, mas foi encontrada em:\n\n" +
                origem + "\n\n" +
                "Copiar para a pasta do aplicativo? Isso torna o Mhiagos Control independente do software de fábrica, " +
                "que poderá ser desinstalado sem perder esses sensores.\n\n" +
                "Destino: " + destino,

                "The library that reads CPU temperature and power is not in this installation, but was found at:\n\n" +
                origem + "\n\n" +
                "Copy it into the application folder? This makes Mhiagos Control independent of the factory software, " +
                "which can then be uninstalled without losing those sensors.\n\n" +
                "Destination: " + destino);
        }

        public static string AdoptFailed(string erro)
        {
            return P("Não foi possível copiar a biblioteca:\n\n" + erro,
                     "Could not copy the library:\n\n" + erro);
        }

        // ---------------- tela de carregamento ----------------

        public static string Starting { get { return P("Iniciando...", "Starting..."); } }
        public static string OpeningSources { get { return P("Abrindo as fontes de sensores...", "Opening the sensor sources..."); } }
        public static string LoadingDriver { get { return P("Carregando o driver de sensores...", "Loading the sensor driver..."); } }
        public static string ClosingIsSafe
        {
            get { return P("fechar não interrompe a inicialização", "closing does not interrupt startup"); }
        }

        // ---------------- bandeja ----------------

        public static string TrayProfiles { get { return P("Perfis", "Profiles"); } }
        public static string TrayConfigure { get { return P("Configurar...", "Settings..."); } }
        public static string TrayPause { get { return P("Pausar", "Pause"); } }
        public static string TrayResume { get { return P("Retomar", "Resume"); } }
        public static string TrayAutostart { get { return P("Iniciar com o Windows", "Start with Windows"); } }
        public static string TrayOpenData { get { return P("Abrir pasta de dados", "Open data folder"); } }
        public static string TrayExit { get { return P("Sair", "Exit"); } }
        public static string TrayStarting { get { return P("Mhiagos Control - iniciando", "Mhiagos Control - starting"); } }
        public static string TrayPaused
        {
            get { return P("Mhiagos Control - pausado (o painel vai apagar)", "Mhiagos Control - paused (the panel will go blank)"); }
        }
        public static string TagIdle { get { return P("  [ocioso]", "  [idle]"); } }
        public static string TagOver { get { return P("  [excede 999]", "  [over 999]"); } }
        public static string TagNoDevice { get { return P("  [painel ausente]", "  [panel missing]"); } }

        // Bateria dos perifericos sem fio, na Visao geral
        public static string NoPeripherals
        {
            get { return P("Nenhum periférico sem fio encontrado (o G HUB precisa estar aberto para o headset)",
                           "No wireless peripherals found (G HUB must be running for the headset)"); }
        }
        public static string PeripheralOff { get { return P("desligado", "off"); } }
        public static string PeripheralCharging { get { return P("carregando", "charging"); } }
        public static string PeripheralNoReading { get { return P("conectado, sem leitura de bateria", "connected, no battery reading"); } }
        public static string BluetoothNone { get { return P("Ligado - nenhum conectado", "On - none connected"); } }
        public static string BluetoothOff { get { return P("Desligado", "Off"); } }

        // Pagina Baterias
        public static string NavBatteries { get { return P("Baterias", "Batteries"); } }
        public static string BatNoDevices
        {
            get { return P("Nenhum periférico com bateria lido ainda. Ligue o headset ou o mouse e a análise começa sozinha.",
                           "No battery reading yet. Turn the headset or mouse on and tracking starts by itself."); }
        }
        public static string BatRemaining { get { return P("Restante estimado", "Estimated remaining"); } }
        public static string BatPace { get { return P("Ritmo por faixa", "Pace by range"); } }
        public static string PaceEven { get { return P("parecido em todas as faixas", "about the same in every range"); } }
        public static string PaceFastest(int de, int ate)
        {
            return P("gasta mais rápido entre " + de + "% e " + ate + "%", "drains fastest between " + de + "% and " + ate + "%");
        }
        public static string BatTotalUse { get { return P("Uso total acompanhado", "Total use tracked"); } }
        public static string OpenGHubToRead { get { return P("abra o G HUB para ler", "open G HUB to read"); } }
        public static string FullyCharged { get { return P("carga completa", "fully charged"); } }
        public static string FullIn(string duracao) { return P("cheio em ~" + duracao, "full in ~" + duracao); }
        public static string HealthFirstMonth { get { return P("1º mês medido", "1st month measured"); } }
        public static string HealthVersus(string duracao, string mes, string variacao)
        {
            return P(variacao + " desde " + mes, variacao + " since " + mes);
        }
        public static string BatteryFullTitle(string nome) { return P(nome + " carregado", nome + " charged"); }
        public static string BatteryFullText { get { return P("Chegou a 100%. Já pode tirar do cabo.", "It reached 100%. You can unplug it."); } }
        public static string BatteryTimeTitle(string nome, string duracao)
        {
            return P(nome + ": ~" + duracao + " de uso restante", nome + ": ~" + duracao + " of use left");
        }
        public static string BatteryTimeText
        {
            get { return P("Pelo seu ritmo de uso, a bateria está perto do fim.", "At your usual pace, the battery is about to run out."); }
        }
        public static string MetricBattery(string nome) { return P("Bateria do " + nome, nome + " battery"); }
        // Widget de bateria na area de trabalho
        public static string TrayBatteryWidget { get { return P("Widget de bateria", "Battery widget"); } }
        public static string WidgetOpenBatteries { get { return P("Abrir a página Baterias", "Open the Batteries page"); } }
        public static string WidgetLayout { get { return P("Formato", "Layout"); } }
        public static string WidgetRings { get { return P("Anéis", "Rings"); } }
        public static string WidgetList { get { return P("Lista", "List"); } }
        public static string WidgetGlass { get { return P("Vidro", "Glass"); } }
        public static string WidgetSharp { get { return P("Nítido (sem desfoque)", "Sharp (no blur)"); } }
        public static string WidgetClear { get { return P("Desfocado", "Blurred"); } }
        public static string WidgetFrosted { get { return P("Fosco", "Frosted"); } }
        public static string WidgetTintAuto { get { return P("Cor do tema do Windows", "Windows theme color"); } }
        public static string WidgetTintDark { get { return P("Escuro", "Dark"); } }
        public static string WidgetTintLight { get { return P("Claro", "Light"); } }
        public static string WidgetOnTop { get { return P("Sempre na frente das janelas", "Always on top"); } }
        public static string WidgetLock { get { return P("Travar posição", "Lock position"); } }
        public static string WidgetHide { get { return P("Ocultar widget", "Hide widget"); } }
        public static string WidgetNoDevices { get { return P("Nenhum periférico com bateria", "No battery devices"); } }
        public static string WidgetCard { get { return P("Widget na área de trabalho", "Desktop widget"); } }
        public static string WidgetButton { get { return P("Widget", "Widget"); } }
        public static string WidgetButtonTip { get { return P("Opções do widget na área de trabalho", "Desktop widget options"); } }
        public static string WidgetShow { get { return P("Mostrar o widget na área de trabalho", "Show the widget on the desktop"); } }
        public static string WidgetSharpShort { get { return P("Nítido", "Sharp"); } }
        public static string WidgetTint { get { return P("Cor", "Color"); } }
        public static string WidgetTintAutoShort { get { return P("Tema do Windows", "Windows theme"); } }
        public static string WidgetBackToCorner { get { return P("Voltar ao canto da tela", "Move back to the corner"); } }
        public static string WidgetCardNote { get { return P("Arraste o widget para mudar de lugar e clique nele para abrir esta página. O botão direito tem as mesmas opções.", "Drag the widget to move it and click it to open this page. Right-click has the same options."); } }
        public static string OpenBatteriesPage { get { return P("Ver detalhes na página Baterias", "See details on the Batteries page"); } }
        public static string BatLastCharge { get { return P("Última carga", "Last charge"); } }
        public static string BatSinceCharge { get { return P("Desde a última carga", "Since last charge"); } }
        public static string BatLastDischarge { get { return P("Última descarga completa", "Last full discharge"); } }
        public static string BatMeasuring { get { return P("medindo (precisa de 10% gastos)", "measuring (needs 10% used)"); } }
        public static string BatNotSeenYet { get { return P("ainda não registrada", "not recorded yet"); } }
        public static string NoReadingFor(TimeSpan t)
        {
            string d = t.TotalSeconds < 60 ? (int)t.TotalSeconds + " s" : Duracao(t);
            return P("sem leitura · " + d, "no reading · " + d);
        }
        public static string BatNoCurve { get { return P("a curva aparece com as primeiras leituras", "the curve shows up with the first readings"); } }
        public static string UseHours(string duracao) { return P(duracao + " de uso", duracao + " of use"); }
        public static string UpTo(int pct) { return P("até " + pct + "%", "up to " + pct + "%"); }
        public static string TookTime(string duracao) { return P("levou " + duracao, "took " + duracao); }
        public static string SinceTracking(string quando) { return P("acompanhando desde " + quando, "tracking since " + quando); }
        public static string EndedAt(string quando) { return P("terminou " + quando, "ended " + quando); }
        public static string Today { get { return P("hoje", "today"); } }
        public static string Yesterday { get { return P("ontem", "yesterday"); } }
        public static string Now { get { return P("agora", "now"); } }
        public static string NavNow { get { return P("agora", "now"); } }
        public static string NavLive { get { return P("ao vivo", "live"); } }
        public static string NavBackToLive { get { return P("Voltar ao vivo", "Back to live"); } }
        public static string HoursAgo(int h) { return P(h + " h atrás", h + " h ago"); }
        // Resumo do cartao de bateria
        public static string StatusCharging { get { return P("Carregando", "Charging"); } }
        public static string StatusOnBattery { get { return P("Na bateria", "On battery"); } }
        public static string StatusOff { get { return P("Desligado", "Off"); } }
        public static string HoursLeft(string duracao) { return P(duracao + " restantes", duracao + " left"); }
        public static string TileRemaining { get { return P("Restante", "Remaining"); } }
        public static string TileFullCharge { get { return P("Carga cheia dura", "Full charge lasts"); } }
        public static string TileChargingSince { get { return P("Carregando desde", "Charging since"); } }
        public static string TileHealth { get { return P("Saúde", "Health"); } }
        public static string GHubSays(string valor) { return "G HUB: " + valor; }
        public static string EstimatedFromUse { get { return P("estimado pelo seu uso", "estimated from your use"); } }
        public static string Measuring { get { return P("medindo", "measuring"); } }
        public static string Needs10Used { get { return P("precisa de 10% gastos", "needs 10% used"); } }
        public static string NeedsWeeks { get { return P("precisa de algumas semanas", "needs a few weeks"); } }
        public static string AverageOfDischarges(int n)
        {
            return P(n == 1 ? "média de 1 descarga" : "média de " + n + " descargas",
                     n == 1 ? "average of 1 discharge" : "average of " + n + " discharges");
        }
        public static string FromPct(int pct) { return P("a partir de " + pct + "%", "from " + pct + "%"); }
        public static string PerChargeShort(string duracao) { return P(duracao + " por carga", duracao + " per charge"); }
        public static string MoreDetails { get { return P("Mais detalhes", "More details"); } }
        public static string LessDetails { get { return P("Menos detalhes", "Fewer details"); } }
        public static string OpenBluetooth { get { return P("Abrir o painel de Bluetooth", "Open the Bluetooth panel"); } }
        public static string OpenApp(string app) { return P("Abrir o " + app, "Open " + app); }

        // Avisos do Windows quando a carga cai
        public static string BatteryLowTitle(string nome, int pct)
        {
            return P(nome + " com " + pct + "% de bateria", nome + " at " + pct + "% battery");
        }
        public static string BatteryLowText { get { return P("A bateria está acabando.", "The battery is running low."); } }
        public static string BatteryCriticalText
        {
            get { return P("Bateria quase no fim, coloque para carregar.", "Battery almost empty, plug it in to charge."); }
        }

        // Identificacao do painel na aba Sobre. So o Temp 6 Pro Black foi
        // testado; quem tiver outro modelo do fabricante copia esta linha para
        // relatar que funcionou.
        public static string PanelNotFound
        {
            get { return P("Painel: nenhum encontrado", "Panel: none found"); }
        }

        public static string PanelUntested
        {
            get
            {
                return P("inclua esta linha ao relatar um problema",
                         "include this line when reporting an issue");
            }
        }

        public static string SensorInitFailed(string message, string logPath)
        {
            return P(
                "Falha ao inicializar os sensores.\n\n" + message +
                "\n\nTemperatura e potência exigem privilégio administrativo." +
                "\n\nDetalhes em:\n" + logPath,
                "Failed to initialize the sensors.\n\n" + message +
                "\n\nTemperature and power require administrator privileges." +
                "\n\nDetails in:\n" + logPath);
        }

        public static string OriginalTaskWarning
        {
            get
            {
                return P(
                    "A tarefa de inicialização do CPU TEMP Monitor original ainda está ativa.\n\n" +
                    "No próximo logon os dois programas vão disputar o painel, e o mostrador vai piscar.\n\n" +
                    "Desativar a tarefa do software original agora?",
                    "The startup task of the original CPU TEMP Monitor is still enabled.\n\n" +
                    "On the next logon both programs will fight over the panel, and the display will flicker.\n\n" +
                    "Disable the original software's task now?");
            }
        }

        // ---------------- previa do mostrador ----------------

        public static string BadgeNoReading { get { return P("sem leitura", "no reading"); } }
        public static string BadgeOver999 { get { return P("excede 999", "over 999"); } }
    }
}
