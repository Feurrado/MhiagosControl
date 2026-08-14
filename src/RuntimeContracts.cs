using System;
using System.Collections.Generic;
using System.Threading;

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

    /// <summary>
    /// Impede reentrada da janela de configuracao. ShowDialog bombeia outra
    /// fila de mensagens; por isso um segundo duplo clique pode chegar enquanto
    /// o primeiro manipulador ainda nao voltou e abrir outro formulario.
    /// </summary>
    internal sealed class WindowOpenGate
    {
        private int _busy;

        public bool TryEnter()
        {
            return Interlocked.CompareExchange(ref _busy, 1, 0) == 0;
        }

        public void Exit()
        {
            Volatile.Write(ref _busy, 0);
        }

        public bool Busy { get { return Volatile.Read(ref _busy) != 0; } }
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
