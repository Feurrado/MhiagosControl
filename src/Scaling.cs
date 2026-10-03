using System;
using LibreHardwareMonitor.Hardware;

namespace MhiagosControl
{
    /// <summary>
    /// Resultado do preparo de um valor para o mostrador de 3 digitos.
    /// </summary>
    public struct PanelValue
    {
        /// <summary>Valor a enviar; null apaga o mostrador.</summary>
        public int? Value;
        /// <summary>Verdadeiro quando o valor foi limitado a 999.</summary>
        public bool Clamped;
        /// <summary>Motivo de estar apagado, para exibir na interface.</summary>
        public string Note;
    }

    /// <summary>
    /// Adequacao de um sensor ao mostrador de 3 digitos (0 a 999, sem ponto
    /// decimal). Clock, RPM, memoria e throughput estouram a faixa sempre;
    /// escalonamos por um divisor e deixamos isso explicito na interface.
    /// </summary>
    public static class Scaling
    {
        public static readonly int[] Divisors = new int[] { 1, 10, 100, 1000 };

        /// <summary>Divisor sugerido para o tipo, quando o perfil nao definiu.</summary>
        public static int Suggest(SensorType type)
        {
            switch (type)
            {
                case SensorType.Clock:      return 10;    // 3700 MHz -> 370 (3,70 GHz)
                case SensorType.Fan:        return 10;    // 1250 RPM -> 125
                case SensorType.Throughput: return 1000;  // bytes/s -> KB/s
                case SensorType.Frequency:  return 10;
                default:                    return 1;     // temperatura, carga, potencia, tensao
            }
        }

        /// <summary>Resolve o divisor: zero no perfil significa automatico.</summary>
        public static int Effective(int configured, SensorEntry sensor)
        {
            if (configured > 0) return configured;
            if (sensor != null && sensor.Type == SensorType.Throughput)
            {
                // Algumas fontes ja publicam KB/s ou MB/s. A escala automatica
                // so converte bytes/s; um divisor escolhido continua literal.
                string unit = sensor.EffectiveUnit;
                if (!string.IsNullOrEmpty(unit) &&
                    !string.Equals(unit, "B/s", StringComparison.OrdinalIgnoreCase)) return 1;
            }
            return sensor != null ? Suggest(sensor.Type) : 1;
        }

        /// <summary>
        /// Prepara o valor de um sensor para o mostrador.
        ///
        /// Sensores sem leitura valida (ausentes ou NaN) apagam o mostrador.
        /// Isso importa: converter NaN para inteiro produz lixo, que antes
        /// virava 0 no painel e passava por medicao legitima.
        /// </summary>
        public static PanelValue Prepare(SensorEntry sensor, int divisor, bool toFahrenheit)
        {
            PanelValue r = new PanelValue();

            if (sensor == null)
            {
                r.Note = "sensor nao encontrado";
                return r;   // Value fica null -> apaga
            }
            if (!sensor.Value.HasValue || float.IsNaN(sensor.Value.Value) || float.IsInfinity(sensor.Value.Value))
            {
                r.Note = "sem leitura";
                return r;
            }

            double d = sensor.Value.Value;

            // Fahrenheit so se aplica a temperatura; o bit apenas acende o simbolo
            if (toFahrenheit && sensor.Type == SensorType.Temperature)
                d = d * 9.0 / 5.0 + 32.0;

            if (divisor > 1) d /= divisor;

            d = Math.Round(d);
            if (d < 0) d = 0;
            if (d > 999) { d = 999; r.Clamped = true; }
            r.Value = (int)d;
            return r;
        }
    }
}
