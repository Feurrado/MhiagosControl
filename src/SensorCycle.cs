namespace MhiagosControl
{
    /// <summary>Leituras do perfil exibido e do perfil que governa alertas.</summary>
    internal struct MonitorReadings
    {
        public SensorEntry Display1;
        public SensorEntry Display2;
        public SensorEntry Alert1;
        public SensorEntry Alert2;
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

        public MonitorReadings Refresh(Profile display, Profile alert, bool sameProfile)
        {
            _sensors.Refresh();
            MonitorReadings r = new MonitorReadings();
            r.Display1 = _sensors.ReadEntry(display.Panel1Id);
            r.Display2 = _sensors.ReadEntry(display.Panel2Id);
            r.Alert1 = sameProfile ? r.Display1 : _sensors.ReadEntry(alert.Panel1Id);
            r.Alert2 = sameProfile ? r.Display2 : _sensors.ReadEntry(alert.Panel2Id);
            return r;
        }
    }
}
