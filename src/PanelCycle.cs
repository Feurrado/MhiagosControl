namespace MhiagosControl
{
    internal sealed class PanelFrame
    {
        public readonly int? Panel1;
        public readonly int? Panel2;
        public readonly bool Fahrenheit;
        public readonly bool Percent;

        public PanelFrame(int? panel1, int? panel2, bool fahrenheit, bool percent)
        {
            Panel1 = panel1; Panel2 = panel2;
            Fahrenheit = fahrenheit; Percent = percent;
        }
    }

    /// <summary>Resultado puro do preparo e envio de um quadro do cooler.</summary>
    internal struct PanelDispatch
    {
        public PanelValue Panel1;
        public PanelValue Panel2;
        public bool Sent;
        public PanelFrame Frame;
    }

    /// <summary>
    /// Limite entre o ciclo de monitoramento e o dispositivo HID. Mantem a regra
    /// de escala e apagamento independente da bandeja/WinForms e permite testar
    /// um ciclo completo com um painel falso.
    /// </summary>
    internal sealed class PanelCycle
    {
        private readonly IPanelDevice _panel;

        public PanelCycle(IPanelDevice panel) { _panel = panel; }

        public PanelDispatch Send(Profile profile, SensorEntry sensor1, SensorEntry sensor2, bool idle)
        {
            PanelDispatch r = Prepare(profile, sensor1, sensor2, idle);
            r.Sent = _panel.Send(r.Frame.Panel1, r.Frame.Panel2,
                                 r.Frame.Fahrenheit, r.Frame.Percent);
            return r;
        }

        public PanelDispatch Prepare(Profile profile, SensorEntry sensor1, SensorEntry sensor2, bool idle)
        {
            if (profile == null) throw new System.ArgumentNullException("profile");
            PanelDispatch r = new PanelDispatch();
            r.Panel1 = Scaling.Prepare(sensor1, Scaling.Effective(profile.Divisor1, sensor1), profile.Fahrenheit);
            r.Panel2 = Scaling.Prepare(sensor2, Scaling.Effective(profile.Divisor2, sensor2), false);
            r.Frame = idle
                ? new PanelFrame(null, null, profile.Fahrenheit, profile.Percent)
                : new PanelFrame(r.Panel1.Value, r.Panel2.Value, profile.Fahrenheit, profile.Percent);
            return r;
        }
    }
}
