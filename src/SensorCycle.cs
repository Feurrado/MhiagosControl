namespace MhiagosControl
{
    /// <summary>Leituras dos dois mostradores do perfil exibido.</summary>
    internal struct MonitorReadings
    {
        public SensorEntry Display1;
        public SensorEntry Display2;
    }

    /// <summary>
    /// Adaptador fino do ciclo para a fonte de sensores. Concentrar estas quatro
    /// consultas deixa TrayContext responsavel por agenda e UI, e deixa a fonte
    /// intercambiavel para testes ou uma futura terceira biblioteca.
    /// </summary>
    internal sealed class SensorCycle
    {
        private readonly ISensorService _sensors;

        public SensorCycle(ISensorService sensors) { _sensors = sensors; }

        public MonitorReadings Refresh(Profile display)
        {
            _sensors.Refresh();
            return Read(display);
        }

        /// <summary>Relê o instantâneo já atualizado sem varrer o hardware.</summary>
        public MonitorReadings Read(Profile display)
        {
            MonitorReadings r = new MonitorReadings();
            r.Display1 = _sensors.ReadEntry(display.Panel1Id);
            r.Display2 = _sensors.ReadEntry(display.Panel2Id);
            return r;
        }
    }
}
