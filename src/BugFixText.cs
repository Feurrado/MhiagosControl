namespace MhiagosControl
{
    public static partial class T
    {
        public static string SensorUnavailable { get { return P("Sensor indisponível", "Sensor unavailable"); } }
        public static string ReadingsPaused { get { return P("Leitura pausada", "Readings paused"); } }
        public static string ReadingsFailed { get { return P("Falha de leitura; tentando novamente", "Reading failed; retrying"); } }
        public static string ReadingsExpired { get { return P("Leitura desatualizada", "Reading expired"); } }
        public static string ReadingsWaiting { get { return P("Aguardando leitura", "Waiting for a reading"); } }
        public static string ExportOwnConfig { get { return P("Escolha outro destino. A exportação não pode substituir a configuração do aplicativo.", "Choose another destination. Export cannot replace the application's configuration."); } }
        public static string InvalidTiming { get { return P("Intervalos inválidos: inatividade aceita 0 a 999 minutos; rodízio aceita 0 (desligado) ou 2 a 999 segundos.", "Invalid intervals: idle accepts 0 to 999 minutes; rotation accepts 0 (disabled) or 2 to 999 seconds."); } }
    }
}
