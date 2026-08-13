using System;
using System.Diagnostics;
using System.Threading;

namespace MhiagosControl
{
    /// <summary>
    /// Escritor unico do HID. Reenvia o ultimo quadro em cadencia independente
    /// para que uma fonte de sensores lenta nao estoure o watchdog do firmware.
    /// </summary>
    internal sealed class PanelKeepalive : IDisposable
    {
        private const int IntervalMs = 800;
        private readonly IPanelDevice _panel;
        private readonly ManualResetEvent _stop = new ManualResetEvent(false);
        private readonly Action<bool> _connectionChanged;
        private Thread _thread;
        private PanelFrame _latest;
        private volatile bool _enabled = true;
        private volatile bool _lastSent;
        private bool? _reported;

        public PanelKeepalive(IPanelDevice panel, Action<bool> connectionChanged)
        {
            if (panel == null) throw new ArgumentNullException("panel");
            _panel = panel;
            _connectionChanged = connectionChanged;
        }

        public bool LastSent { get { return _lastSent; } }
        public bool Enabled { get { return _enabled; } set { _enabled = value; } }

        public void Publish(PanelFrame frame)
        {
            if (frame == null) return;
            Interlocked.Exchange(ref _latest, frame);
        }

        public void Start()
        {
            if (_thread != null) return;
            _thread = new Thread(Run);
            _thread.IsBackground = true;
            _thread.Name = "PanelKeepalive";
            _thread.Start();
        }

        private void Run()
        {
            Stopwatch clock = Stopwatch.StartNew();
            long next = 0;
            while (!_stop.WaitOne(0))
            {
                next += IntervalMs;
                if (_enabled) DispatchOnce();

                long delay = next - clock.ElapsedMilliseconds;
                if (delay < 0)
                {
                    next += ((-delay / IntervalMs) + 1) * IntervalMs;
                    delay = next - clock.ElapsedMilliseconds;
                }
                if (_stop.WaitOne((int)Math.Max(1, delay))) break;
            }
            Log.Write("thread de keepalive encerrada");
        }

        internal bool DispatchOnce()
        {
            PanelFrame frame = Interlocked.CompareExchange(ref _latest, null, null);
            if (frame == null) return false;

            bool sent = false;
            try
            {
                sent = _panel.Send(frame.Panel1, frame.Panel2, frame.Fahrenheit, frame.Percent);
            }
            catch (Exception ex) { Log.Error("envio do keepalive", ex); }
            _lastSent = sent;

            if (!_reported.HasValue || _reported.Value != sent)
            {
                _reported = sent;
                if (_connectionChanged != null) _connectionChanged(sent);
            }
            return sent;
        }

        public void Stop() { _stop.Set(); }

        public bool Wait(int milliseconds)
        {
            Thread thread = _thread;
            return thread == null || !thread.IsAlive || thread.Join(Math.Max(0, milliseconds));
        }

        public void Dispose()
        {
            Stop();
            if (_thread == null || !_thread.IsAlive) _stop.Dispose();
        }
    }
}
