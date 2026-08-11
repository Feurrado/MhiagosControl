using System;
using System.Globalization;
using System.Threading;

namespace MhiagosControl
{
    /// <summary>
    /// Sonda do protocolo do painel. Manda quadros crus e deixa varrer o que
    /// ainda nao foi mapeado:
    ///
    ///   - codigos de digito acima de 0x0F (0..9 = numeros, 0x0A..0x0F = apagado;
    ///     0x10..0xFF nunca foram testados)
    ///   - os 56 bytes que o software original sempre zera
    ///   - outros ReportIDs alem do 0x07
    ///   - a cadencia maxima que o firmware aceita
    ///
    /// Um laco de fundo reenvia o quadro atual a cada 400 ms - sem isso o
    /// watchdog apaga o painel enquanto se olha para ele.
    ///
    /// Nao precisa de elevacao: falar HID com o dispositivo nao envolve driver.
    /// </summary>
    public static class Probe
    {
        private static readonly byte[] _frame = new byte[64];
        private static readonly object _lock = new object();
        private static volatile bool _run = true;
        private static volatile int _period = 400;

        public static int Main(string[] args)
        {
            HidPanel panel = new HidPanel();
            if (!panel.Open())
            {
                Console.WriteLine("cooler nao encontrado (VID 1A2C / PID 4984). Esta conectado?");
                return 1;
            }
            Console.WriteLine("aberto: " + panel.DevicePath);
            // O quadro de teste sobe antes de qualquer comando, e uma thread o
            // reenvia sem parar. Sem dizer isso aqui, o painel parado em 123/456
            // parece defeito - foi o que aconteceu na primeira vez.
            Console.WriteLine("o painel vai mostrar 123 °C / 456 W - e o quadro de teste, nao defeito.");
            Console.WriteLine("'un' mapeia os indicadores de unidade; 'q' apaga e sai.");

            lock (_lock)
            {
                _frame[0] = 0x07;
                _frame[1] = 1; _frame[2] = 2; _frame[3] = 3;
                _frame[4] = 0x00;
                _frame[5] = 4; _frame[6] = 5; _frame[7] = 6;
            }

            Thread keeper = new Thread(delegate ()
            {
                byte[] copy = new byte[64];
                while (_run)
                {
                    lock (_lock) Array.Copy(_frame, copy, 64);
                    panel.SendRaw(copy);
                    Thread.Sleep(_period);
                }
            });
            keeper.IsBackground = true;
            keeper.Start();

            Ajuda();
            while (true)
            {
                Console.Write("\n> ");
                string line = Console.ReadLine();
                if (line == null) break;
                line = line.Trim();
                if (line.Length == 0) { Mostrar(); continue; }
                if (line == "q") break;
                try { if (!Comando(line, panel)) Ajuda(); }
                catch (Exception ex) { Console.WriteLine("erro: " + ex.Message); }
            }

            _run = false;
            Thread.Sleep(_period + 100);
            panel.Send(null, null, false, false);   // apaga antes de sair
            panel.Close();
            return 0;
        }

        private static bool Comando(string line, HidPanel panel)
        {
            string[] p = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            switch (p[0])
            {
                case "b":                                   // b <indice> <hex>
                    Set(int.Parse(p[1]), Hex(p[2]));
                    Mostrar();
                    return true;

                case "v":                                   // v <indice>  - varredura manual
                    Varrer(int.Parse(p[1]), -1);
                    return true;

                case "va":                                  // va <indice> <ms> - varredura automatica
                    Varrer(int.Parse(p[1]), p.Length > 2 ? int.Parse(p[2]) : 500);
                    return true;

                case "r":                                   // r 07 01 02 03 00 04 05 06
                    lock (_lock)
                    {
                        Array.Clear(_frame, 0, 64);
                        for (int i = 0; i < p.Length - 1 && i < 64; i++) _frame[i] = Hex(p[i + 1]);
                    }
                    Mostrar();
                    return true;

                case "hz":                                  // hz <ms> - cadencia do reenvio
                    _period = Math.Max(5, int.Parse(p[1]));
                    Console.WriteLine("reenvio a cada " + _period + " ms");
                    return true;

                case "anim":                                // anim <ms> - contador rapido
                    Animar(p.Length > 1 ? int.Parse(p[1]) : 80);
                    return true;

                case "un":                                  // mapeia os indicadores de unidade
                    Unidades();
                    return true;

                case "ids":                                 // procura outros ReportIDs
                    Ids(panel);
                    return true;

                case "?":
                    return false;
            }
            return false;
        }

        private static void Set(int idx, byte v)
        {
            if (idx < 0 || idx > 63) throw new ArgumentException("indice de 0 a 63");
            lock (_lock) _frame[idx] = v;
        }

        /// <summary>
        /// Percorre 0x00..0xFF num byte. Com passo manual da para anotar o que
        /// aparece; com passo automatico da para ver de longe se muda alguma coisa.
        /// </summary>
        private static void Varrer(int idx, int ms)
        {
            byte original;
            lock (_lock) original = _frame[idx];

            Console.WriteLine(ms < 0
                ? "Enter avanca, 'x' encerra. byte[" + idx + "]"
                : "varrendo byte[" + idx + "] a cada " + ms + " ms; tecla encerra");

            for (int v = 0; v <= 255; v++)
            {
                Set(idx, (byte)v);
                Console.Write("\r  byte[" + idx + "] = 0x" + v.ToString("X2") + " (" + v + ")     ");
                if (ms < 0)
                {
                    string s = Console.ReadLine();
                    if (s != null && s.Trim() == "x") break;
                }
                else
                {
                    Thread.Sleep(ms);
                    if (Console.KeyAvailable) { Console.ReadKey(true); break; }
                }
            }
            Set(idx, original);
            Console.WriteLine("\nfim; byte[" + idx + "] restaurado para 0x" + original.ToString("X2"));
        }

        /// <summary>
        /// Mapeia os dois indicadores de unidade, um nibble de cada vez.
        ///
        /// De report[4] so bit0 (°F) e bit4 (%) foram levantados. A simetria -
        /// um bit conhecido em cada nibble - sugere que cada nibble comanda um
        /// indicador, com tres bits sem mapa em cada. Se existir um codigo que
        /// apaga o simbolo, em vez de so alternar entre os dois, e ali.
        ///
        /// Anda passo a passo esperando Enter porque a resposta e visual: o que
        /// importa e qual simbolo esta aceso, ou nenhum.
        /// </summary>
        private static void Unidades()
        {
            byte original;
            lock (_lock)
            {
                original = _frame[4];
                // Digitos visiveis de proposito: com o mostrador apagado nao da
                // para distinguir "o indicador apagou" de "o quadro inteiro caiu".
                _frame[1] = 1; _frame[2] = 2; _frame[3] = 3;
                _frame[5] = 4; _frame[6] = 5; _frame[7] = 6;
            }

            Console.WriteLine();
            Console.WriteLine("Olhe o cooler a cada passo e anote: °C, °F, os dois, ou nenhum?");
            Console.WriteLine("Os digitos ficam em 123 e 456 - se eles sumirem, o quadro caiu.");
            Console.WriteLine("Enter avanca, 'x' encerra.");

            if (Nibble("de cima  (°C / °F)  - nibble baixo", 0))
                Nibble("de baixo (%  / W )  - nibble alto", 4);

            Set(4, original);
            Console.WriteLine();
            Console.WriteLine("fim; report[4] restaurado para 0x" + original.ToString("X2"));
        }

        private static bool Nibble(string qual, int deslocamento)
        {
            Console.WriteLine();
            Console.WriteLine("--- indicador " + qual + " ---");
            for (int v = 0; v <= 15; v++)
            {
                byte b = (byte)(v << deslocamento);
                Set(4, b);
                Console.Write("  report[4] = 0x" + b.ToString("X2") + "   bits " + Bits(v) + "   -> ");
                string s = Console.ReadLine();
                if (s != null && s.Trim() == "x") return false;
            }
            return true;
        }

        private static string Bits(int v)
        {
            char[] c = new char[4];
            for (int i = 0; i < 4; i++) c[i] = ((v >> (3 - i)) & 1) == 1 ? '1' : '0';
            return new string(c);
        }

        /// <summary>
        /// Escreve os seis digitos em sequencia rapida. Serve para medir na
        /// pratica ate onde o firmware acompanha: se o mostrador embolar ou
        /// piscar, passou do limite - e nao ha animacao viavel abaixo disso.
        /// </summary>
        private static void Animar(int ms)
        {
            int guardado = _period;
            _period = Math.Max(5, ms);
            Console.WriteLine("quadro a cada " + _period + " ms; tecla encerra");

            for (int n = 0; ; n++)
            {
                lock (_lock)
                {
                    for (int i = 0; i < 3; i++) _frame[1 + i] = (byte)((n + i) % 10);
                    for (int i = 0; i < 3; i++) _frame[5 + i] = (byte)((n + i + 5) % 10);
                }
                Thread.Sleep(_period);
                if (Console.KeyAvailable) { Console.ReadKey(true); break; }
            }
            _period = guardado;
            Console.WriteLine("fim");
        }

        /// <summary>
        /// Tenta todos os ReportIDs de Feature. Um que aceite alem do 0x07
        /// seria a porta para qualquer coisa que o software original nao usa.
        /// </summary>
        private static void Ids(HidPanel panel)
        {
            bool guardado = _run;
            _run = false;
            Thread.Sleep(_period + 50);

            Console.WriteLine("ReportIDs aceitos:");
            byte[] f = new byte[64];
            int achados = 0;
            for (int id = 1; id <= 255; id++)
            {
                Array.Clear(f, 0, 64);
                f[0] = (byte)id;
                if (panel.SendRaw(f)) { Console.WriteLine("  0x" + id.ToString("X2")); achados++; }
            }
            if (achados == 0) Console.WriteLine("  (nenhum - nem o 0x07? o dispositivo pode ter caido)");

            _run = guardado;
        }

        private static void Mostrar()
        {
            lock (_lock)
            {
                Console.Write("quadro:");
                for (int i = 0; i < 8; i++) Console.Write(" " + _frame[i].ToString("X2"));
                bool resto = false;
                for (int i = 8; i < 64; i++) if (_frame[i] != 0) resto = true;
                Console.WriteLine(resto ? " ... (bytes 8+ nao zerados)" : " 00 x56");
            }
        }

        private static byte Hex(string s)
        {
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
            return byte.Parse(s, NumberStyles.HexNumber);
        }

        private static void Ajuda()
        {
            Console.WriteLine();
            Console.WriteLine("  b <i> <hex>   escreve o byte i do quadro   (b 1 0F)");
            Console.WriteLine("  v <i>         varre 00..FF no byte i, passo a passo");
            Console.WriteLine("  va <i> [ms]   varre sozinho                (va 4 300)");
            Console.WriteLine("  r <hex...>    quadro inteiro do zero       (r 07 08 08 08 11 08 08 08)");
            Console.WriteLine("  hz <ms>       cadencia do reenvio          (hz 100)");
            Console.WriteLine("  anim [ms]     contador nos seis digitos    (anim 60)");
            Console.WriteLine("  un            mapeia os indicadores °C/°F e %/W");
            Console.WriteLine("  ids           procura outros ReportIDs");
            Console.WriteLine("  <enter>       mostra o quadro atual");
            Console.WriteLine("  q             sai e apaga o painel");
            Console.WriteLine();
            Console.WriteLine("  mapa: [0]=ReportID  [1..3]=painel 1  [4]=flags  [5..7]=painel 2");
            Console.WriteLine("        digitos 00..09 = numero, 0A..0F = apagado, 10..FF = ?");
            Console.WriteLine("        flags bit0=Fahrenheit bit4=%, os outros seis nao fazem nada");
        }
    }
}
