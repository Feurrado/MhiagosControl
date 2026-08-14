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
        private const int KeepaliveMs = 800;
        internal const int MinDispatchMs = 250;
        private readonly IPanelDevice _panel;
        private readonly ManualResetEvent _stop = new ManualResetEvent(false);
        private readonly AutoResetEvent _changed = new AutoResetEvent(false);
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
        public bool Enabled
        {
            get { return _enabled; }
            set { _enabled = value; if (value) _changed.Set(); }
        }

        public void Publish(PanelFrame frame)
        {
            if (frame == null) return;
            PanelFrame previous = Interlocked.Exchange(ref _latest, frame);
            if (!Same(previous, frame)) _changed.Set();
        }

        private static bool Same(PanelFrame a, PanelFrame b)
        {
            return a != null && b != null && a.Panel1 == b.Panel1 && a.Panel2 == b.Panel2 &&
                   a.Fahrenheit == b.Fahrenheit && a.Percent == b.Percent;
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
            long lastDispatch = -MinDispatchMs;
            long keepaliveAt = 0;
            WaitHandle[] signals = { _stop, _changed };
            while (true)
            {
                long now = clock.ElapsedMilliseconds;
                int timeout = _enabled ? (int)Math.Max(1, keepaliveAt - now)
                                       : Timeout.Infinite;
                int reason = WaitHandle.WaitAny(signals, timeout);
                if (reason == 0) break;
                if (!_enabled) continue;

                now = clock.ElapsedMilliseconds;
                long remaining = MinDispatchMs - (now - lastDispatch);
                if (remaining > 0 && _stop.WaitOne((int)remaining)) break;

                long dispatchStarted = clock.ElapsedMilliseconds;
                DispatchOnce();
                // Limite entre inícios, não depois do retorno: o SET_FEATURE
                // leva ~9,5 ms neste painel. O intervalo é contado entre os
                // inícios para manter os 250 ms pedidos sem acumular esse custo.
                lastDispatch = dispatchStarted;
                keepaliveAt = clock.ElapsedMilliseconds + KeepaliveMs;
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
            if (_thread == null || !_thread.IsAlive)
            {
                _changed.Dispose();
                _stop.Dispose();
            }
        }
    }
}
