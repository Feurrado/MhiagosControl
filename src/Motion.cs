using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// Tempos e curvas do movimento, num so lugar.
    ///
    /// Tres duracoes (rapida 83 ms para resposta a toque, media 167 ms para
    /// troca de conteudo, lenta 250 ms para abrir/fechar painel) e tres curvas:
    /// Padrao (0,4 / 0 / 0,2 / 1, a do painel que recolhe), Entrada
    /// (desacelera: o que aparece chega e assenta) e Saida (acelera: o que
    /// sai ganha velocidade). O relogio e um Timer so, de 10 ms - o do Windows
    /// anda em passos de 15,6 ms, e 16 cairia em 31 ms -, que serve a todas as
    /// animacoes de estado; cada uma recebe o tempo pelo Stopwatch, entao
    /// atraso do timer vira menos quadros, e nao animacao mais longa.
    /// </summary>
    internal static class Motion
    {
        public const int Rapida = 83;
        public const int Media = 167;
        public const int Lenta = 250;

        /// <summary>cubic-bezier(0.4, 0, 0.2, 1): sai rapido e assenta devagar.</summary>
        public static float Padrao(float t) { return Curva(t, 0.4, 0.0, 0.2, 1.0); }

        /// <summary>cubic-bezier(0, 0, 0, 1): desacelera; para o que entra.</summary>
        public static float Entrada(float t) { return Curva(t, 0.0, 0.0, 0.0, 1.0); }

        /// <summary>cubic-bezier(1, 0, 1, 1): acelera; para o que sai.</summary>
        public static float Saida(float t) { return Curva(t, 1.0, 0.0, 1.0, 1.0); }

        /// <summary>
        /// A curva e dada por x(s) e y(s); o tempo entra como x e precisa
        /// virar s antes de y sair. Newton com bissecao de reserva.
        /// </summary>
        public static float Curva(float t, double x1, double y1, double x2, double y2)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            double s = t;
            for (int i = 0; i < 8; i++)
            {
                double x = Bezier(s, x1, x2) - t;
                if (Math.Abs(x) < 1e-6) break;
                double d = Derivada(s, x1, x2);
                if (Math.Abs(d) < 1e-6) break;
                s -= x / d;
            }
            if (s < 0 || s > 1 || Math.Abs(Bezier(s, x1, x2) - t) > 1e-4)
            {
                double baixo = 0, alto = 1;
                s = t;
                for (int i = 0; i < 40; i++)
                {
                    double x = Bezier(s, x1, x2);
                    if (Math.Abs(x - t) < 1e-6) break;
                    if (x < t) baixo = s; else alto = s;
                    s = (baixo + alto) / 2;
                }
            }
            return (float)Bezier(s, y1, y2);
        }

        private static double Bezier(double s, double p1, double p2)
        {
            double u = 1 - s;
            return 3 * u * u * s * p1 + 3 * u * s * s * p2 + s * s * s;
        }

        private static double Derivada(double s, double p1, double p2)
        {
            double u = 1 - s;
            return 3 * u * u * p1 + 6 * u * s * (p2 - p1) + 3 * s * s * (1 - p2);
        }

        // ---- relogio unico ----

        private static readonly Timer _relogio = CriarRelogio();
        private static readonly Stopwatch _cronometro = Stopwatch.StartNew();
        private static readonly List<Func<long, bool>> _assinantes = new List<Func<long, bool>>();

        private static Timer CriarRelogio()
        {
            Timer t = new Timer();
            t.Interval = 10;
            t.Tick += delegate { Batida(); };
            return t;
        }

        private static void Batida()
        {
            _ultimoTique = _cronometro.ElapsedMilliseconds;
            if (Tiques != null) Tiques.Add(_ultimoTique);
            Passo(_ultimoTique);
        }

        // ---- batida na cadencia da tela ----
        //
        // O Timer do WinForms e WM_TIMER: a mensagem de menor prioridade, presa ao
        // relogio do Windows (15,6 ms), entao os intervalos alternavam entre 15 e 31 ms
        // (~40 a 55 fps, com tranco) e nunca casavam com o vblank. Com a thread de
        // interface presa, o relogio passa a bater quando o DWM compoe um quadro
        // (DwmFlush bloqueia ate la): uma batida por quadro da tela, 60, 120 ou 144 Hz.
        // A mensagem e postada (prioridade acima da pintura) e uma so fica pendente,
        // para uma interface ocupada nao acumular fila. Sem DWM ou sem interface presa
        // (os testes), segue o Timer de sempre.

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmFlush();

        private static System.Threading.Thread _vsync;
        private static readonly System.Threading.ManualResetEvent _vsyncLigado = new System.Threading.ManualResetEvent(false);
        private static int _batidaPendente;
        private static volatile bool _vsyncFalhou;

        internal static bool UsaVsync { get { return _interface != null && !_vsyncFalhou && VsyncPermitido; } }
        /// <summary>Testes e diagnostico podem forcar o Timer.</summary>
        internal static bool VsyncPermitido = true;

        private static bool RelogioLigado { get { return UsaVsync ? _vsyncLigado.WaitOne(0) : _relogio.Enabled; } }

        private static void LigarRelogio()
        {
            if (!UsaVsync) { if (!_relogio.Enabled) _relogio.Start(); return; }
            if (_vsync == null)
            {
                _vsync = new System.Threading.Thread(LacoDoVsync);
                _vsync.IsBackground = true;
                _vsync.Name = "Motion vsync";
                _vsync.Start();
            }
            _vsyncLigado.Set();
        }

        private static void PararRelogio()
        {
            _relogio.Stop();
            _vsyncLigado.Reset();
        }

        private static void LacoDoVsync()
        {
            while (true)
            {
                _vsyncLigado.WaitOne();
                int hr;
                try { hr = DwmFlush(); } catch { hr = -1; }
                if (hr != 0)
                {
                    // sem DWM: cai no Timer, e a thread sai
                    _vsyncFalhou = true;
                    System.Threading.SynchronizationContext ctx = _interface;
                    if (ctx != null) ctx.Post(delegate { _vsyncLigado.Reset(); _vsync = null; if (_assinantes.Count > 0 && !_relogio.Enabled) _relogio.Start(); }, null);
                    return;
                }
                if (!_vsyncLigado.WaitOne(0)) continue;
                if (System.Threading.Interlocked.Exchange(ref _batidaPendente, 1) == 1) continue;
                System.Threading.SynchronizationContext c = _interface;
                if (c != null) c.Post(delegate { System.Threading.Interlocked.Exchange(ref _batidaPendente, 0); if (_vsyncLigado.WaitOne(0)) Batida(); }, null);
            }
        }

        private static long _ultimoTique;
        /// <summary>Medicao: com uma lista aqui, cada batida do relogio anota o instante (ms).</summary>
        internal static List<long> Tiques;
        private static int _threadDaInterface;
        private static System.Threading.SynchronizationContext _interface;

        /// <summary>
        /// Prende o relogio na thread da interface (a bandeja e a janela chamam ao
        /// nascer). O Timer do WinForms passa a bater na thread que o liga: ligado
        /// por engano numa thread sem fila de mensagens, nunca bateria, e nenhuma
        /// animacao andaria. Assinar fora dela e repassado para ela.
        /// </summary>
        public static void PrenderNaInterface()
        {
            // A bandeja nasce antes de qualquer controle: ainda nao ha contexto do WinForms
            // nesta thread. Sem ele, NaInterface rodava o pedido na thread de fundo - e foi
            // assim que a busca de cidade derrubou o app (controle criado fora da interface).
            if (System.Threading.SynchronizationContext.Current == null)
                System.Threading.SynchronizationContext.SetSynchronizationContext(new System.Windows.Forms.WindowsFormsSynchronizationContext());
            _interface = System.Threading.SynchronizationContext.Current;
            _threadDaInterface = System.Threading.Thread.CurrentThread.ManagedThreadId;
        }

        /// <summary>
        /// Roda m na thread da interface: direto se ja esta nela (ou se ninguem a
        /// prendeu, como nos testes), senao repassado. Para quem recebe aviso de
        /// outra thread e ainda nao tem handle para usar BeginInvoke.
        /// </summary>
        public static void NaInterface(System.Windows.Forms.MethodInvoker m)
        {
            if (m == null) return;
            if (_interface == null || System.Threading.Thread.CurrentThread.ManagedThreadId == _threadDaInterface) { m(); return; }
            _interface.Post(delegate { m(); }, null);
        }

        /// <summary>Diagnostico: com o arquivo "animacao-debug" na pasta de dados, as animacoes do ar anotam no log.</summary>
        internal static bool Diagnostico;

        internal static void Anotar(string texto) { if (Diagnostico) Log.Write("animacao: " + texto + " (relogio " + (RelogioLigado ? (UsaVsync ? "ligado (vsync)" : "ligado") : "parado") + ", efeitos " + (Ui.Animacoes ? "sim" : "nao") + ", assinantes " + _assinantes.Count + ")"); }

        /// <summary>Milissegundos do relogio de movimento (so cresce).</summary>
        public static long Agora { get { return _cronometro.ElapsedMilliseconds; } }

        /// <summary>
        /// Registra um passo. O delegate recebe Agora e devolve true enquanto
        /// ainda quer quadros; ao devolver false sai do relogio, e o timer
        /// para quando ninguem sobra.
        /// </summary>
        public static void Assinar(Func<long, bool> passo)
        {
            if (passo == null) return;
            if (_interface != null && System.Threading.Thread.CurrentThread.ManagedThreadId != _threadDaInterface)
            {
                _interface.Post(delegate { Assinar(passo); }, null);
                return;
            }
            if (!_assinantes.Contains(passo)) _assinantes.Add(passo);
            // Ligado mas sem bater ha um tempo (timer preso a outra thread, ou parado pelo sistema): religa aqui.
            if (RelogioLigado && _cronometro.ElapsedMilliseconds - _ultimoTique > 300) { PararRelogio(); Anotar("relogio religado"); }
            if (!RelogioLigado) { _ultimoTique = _cronometro.ElapsedMilliseconds; LigarRelogio(); }
        }

        internal static int Assinantes { get { return _assinantes.Count; } }

        /// <summary>Um passo do relogio; separado para os testes.</summary>
        internal static void Passo(long agora)
        {
            for (int i = _assinantes.Count - 1; i >= 0; i--)
            {
                if (i >= _assinantes.Count) continue;
                bool continua;
                try { continua = _assinantes[i](agora); }
                catch { continua = false; }
                if (!continua && i < _assinantes.Count) _assinantes.RemoveAt(i);
            }
            if (_assinantes.Count == 0) PararRelogio();
        }

        /// <summary>
        /// Progresso 0..1 de um trecho que comecou em 'inicio', ou 1 se o
        /// Windows tem efeitos desligados ("reduzir movimento").
        /// </summary>
        public static float Progresso(long inicio, long agora, int duracaoMs)
        {
            if (!Ui.Animacoes || duracaoMs <= 0) return 1f;
            return Math.Max(0f, Math.Min(1f, (agora - inicio) / (float)duracaoMs));
        }
    }

    /// <summary>
    /// Um valor de 0 a 1 que anda ate o alvo no relogio unico: o hover e o
    /// pressionado de um controle desenhado a mao. Reaponta no meio do caminho
    /// (sai do ponto em que esta); com "reduzir movimento" ou controle sem
    /// janela, vai direto. O dono e repintado a cada quadro.
    /// </summary>
    internal sealed class Transicao
    {
        private readonly Control _dono;
        private readonly int _duracao;
        private readonly Func<Rectangle> _area;
        private float _de, _ate, _valor;
        private long _inicio;
        private bool _assinada;

        public Transicao(Control dono) : this(dono, Motion.Rapida) { }

        public Transicao(Control dono, int duracaoMs) : this(dono, duracaoMs, null) { }

        /// <summary>area: so este retangulo e repintado a cada quadro (nulo: o controle todo).</summary>
        public Transicao(Control dono, int duracaoMs, Func<Rectangle> area)
        {
            _dono = dono;
            _duracao = duracaoMs;
            _area = area;
        }

        public float Valor { get { return _valor; } }
        public bool Animando { get { return _valor != _ate; } }

        /// <summary>Vai a v sem animar (estado inicial, sem transicao ao abrir).</summary>
        public void Fixar(float v)
        {
            _valor = _de = _ate = v;
            _assinada = false;
        }

        /// <summary>Volta a zero sem animar (para recomecar o fade em outro alvo).</summary>
        public void Zerar()
        {
            _valor = _de = _ate = 0f;
            _assinada = false;
        }

        public void Ir(float alvo)
        {
            if (alvo == _ate && (_assinada || _valor == alvo)) return;
            _ate = alvo;
            if (!Ui.Animacoes || _dono == null || !_dono.IsHandleCreated || !_dono.Visible)
            {
                _valor = alvo;
                _assinada = false;
                if (_dono != null) _dono.Invalidate();
                return;
            }
            _de = _valor;
            _inicio = Motion.Agora;
            if (!_assinada) { _assinada = true; Motion.Assinar(Passo); }
        }

        private void Repintar()
        {
            Rectangle r = _area != null ? _area() : Rectangle.Empty;
            if (r.Width > 0 && r.Height > 0) _dono.Invalidate(r); else _dono.Invalidate();
        }

        /// <summary>Um quadro; true enquanto falta caminho. Separado para os testes.</summary>
        internal bool Passo(long agora)
        {
            if (_dono == null || _dono.IsDisposed) { _assinada = false; return false; }
            float t = Motion.Progresso(_inicio, agora, (int)Math.Max(1, _duracao * Math.Abs(_ate - _de)));
            _valor = _de + (_ate - _de) * Motion.Padrao(t);
            if (t >= 1f) { _valor = _ate; _assinada = false; }
            Repintar();
            return _assinada;
        }
    }
}

namespace MhiagosControl
{
    /// <summary>
    /// Relogio de quadros de uma animacao: Start/Stop/Enabled como o Timer, mas
    /// batendo no relogio unico do Motion (uma batida por quadro da tela). O
    /// valor da animacao vem do cronometro dela, nao daqui.
    /// </summary>
    internal sealed class RelogioDeQuadros
    {
        private readonly Action _tique;
        private readonly Func<long, bool> _passo;
        private bool _ligado;

        public RelogioDeQuadros(Action tique)
        {
            _tique = tique;
            _passo = delegate(long agora)
            {
                if (!_ligado) return false;
                _tique();
                return _ligado;
            };
        }

        /// <summary>Intervalo do Timer de reserva (sem vsync): abaixo do passo de 15,6 ms do Windows.</summary>
        public int Interval { get { return 10; } }
        public bool Enabled { get { return _ligado; } }

        public void Start() { _ligado = true; Motion.Assinar(_passo); }
        public void Stop() { _ligado = false; }
        public void Dispose() { _ligado = false; }
    }
}
