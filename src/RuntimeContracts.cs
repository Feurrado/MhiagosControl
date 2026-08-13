using System;
using System.Collections.Generic;

namespace MhiagosControl
{
    /// <summary>
    /// Contrato que a orquestracao usa para ler sensores. O aplicativo depende
    /// dele, nao de HWiNFO, LibreHardwareMonitor ou RTSS diretamente; assim o
    /// mesmo ciclo pode ser exercitado com uma fonte falsa em testes.
    /// </summary>
    public interface ISensorService : IDisposable
    {
        bool ShowAll { get; set; }
        void Open();
        void Refresh();
        void Focar(IEnumerable<string> ids);
        List<SensorEntry> List();
        Dictionary<string, float> Snapshot();
        SensorEntry ReadEntry(string identifier);
    }

    /// <summary>Canal de saida do monitoramento para o painel fisico.</summary>
    public interface IPanelDevice
    {
        bool Send(int? panel1, int? panel2, bool fahrenheit, bool percent);
        void Close();
    }

    /// <summary>
    /// Dados vivos que a janela de configuracao consulta. A janela nao conhece
    /// TrayContext nem trava de hardware; o hospedeiro decide como obter ambos.
    /// </summary>
    public interface ISettingsData
    {
        Dictionary<string, float> CurrentSnapshot();
        List<SensorEntry> RefreshSensorList();
        void SetShowAllSensors(bool showAll);
    }

    /// <summary>Uma origem independente de leituras brutas.</summary>
    internal interface ISensorSource : IDisposable
    {
        string Name { get; }
        bool Available { get; }
        bool LastReadComplete { get; }
        bool Open();
        void Refresh();
        List<SensorEntry> Read(IEnumerable<string> focusedIds);
    }
}
