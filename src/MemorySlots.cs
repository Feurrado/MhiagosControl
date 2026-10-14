using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace MhiagosControl
{
    /// <summary>Um slot de memoria da placa, ocupado ou nao.</summary>
    public class SlotDeMemoria
    {
        /// <summary>Nome curto do slot, como impresso na placa: "A1", "B2".</summary>
        public string Nome;

        /// <summary>O que a BIOS chama de Device Locator, cru: "DIMM 0", "DIMM_A1".</summary>
        public string Localizador;

        /// <summary>O Bank Locator, cru: "P0 CHANNEL A", "BANK 0".</summary>
        public string Banco;

        /// <summary>Tamanho do pente; zero e slot vazio.</summary>
        public ulong Bytes;

        public string Fabricante, Peca;

        public bool Ocupado { get { return Bytes > 0; } }
    }

    /// <summary>
    /// Os slots de memoria da placa, inclusive os vazios, lidos da tabela
    /// SMBIOS tipo 17.
    ///
    /// O Win32_PhysicalMemory so traz os pentes instalados. Com ele o desenho
    /// sabia QUANTOS estavam ocupados, mas nao ONDE, e acendia os primeiros da
    /// esquerda para a direita: dois pentes em A2 e B2 apareciam em A1 e A2. A
    /// tabela da BIOS lista todos os slots, cada um com o seu nome, e e a mesma
    /// fonte de onde o Windows tira o Win32_PhysicalMemory - so que sem o filtro.
    ///
    /// GetSystemFirmwareTable("RSMB") nao pede administrador.
    /// </summary>
    public static class SlotsDeMemoria
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint GetSystemFirmwareTable(uint provider, uint id, byte[] buffer, uint size);

        private const uint RSMB = 0x52534D42;   // 'RSMB', em big endian como a API pede

        /// <summary>
        /// Os slots nomeados e em ordem, ou nulo quando a tabela nao veio ou os
        /// nomes fogem do padrao - e entao o desenho volta ao antigo, que ao
        /// menos nao finge saber o lugar.
        /// </summary>
        public static List<SlotDeMemoria> Ler()
        {
            try
            {
                uint n = GetSystemFirmwareTable(RSMB, 0, null, 0);
                if (n == 0 || n > 4 * 1024 * 1024) return null;
                byte[] buf = new byte[n];
                if (GetSystemFirmwareTable(RSMB, 0, buf, n) != n) return null;

                // RawSMBIOSData: 4 bytes de versao, 4 de tamanho e a tabela.
                if (buf.Length < 8) return null;
                int tam = BitConverter.ToInt32(buf, 4);
                if (tam <= 0 || 8 + tam > buf.Length) return null;
                byte[] tabela = new byte[tam];
                Array.Copy(buf, 8, tabela, 0, tam);

                List<SlotDeMemoria> slots = DaTabela(tabela);
                return Nomear(slots) ? slots : null;
            }
            catch (Exception ex)
            {
                Log.Write("slots de memoria: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// As estruturas tipo 17 da tabela crua, na ordem em que vem.
        ///
        /// Fica de fora o que pertence a um arranjo que nao e a memoria do
        /// sistema (tipo 16 com "Use" diferente de 3): algumas placas descrevem
        /// a flash da BIOS ou a cache como um "Memory Device" tambem.
        /// </summary>
        internal static List<SlotDeMemoria> DaTabela(byte[] t)
        {
            List<SlotDeMemoria> saida = new List<SlotDeMemoria>();
            Dictionary<int, bool> arranjos = new Dictionary<int, bool>();
            List<KeyValuePair<int, SlotDeMemoria>> brutos = new List<KeyValuePair<int, SlotDeMemoria>>();

            int i = 0;
            while (i + 4 <= t.Length)
            {
                int tipo = t[i], tam = t[i + 1];
                if (tam < 4 || i + tam > t.Length) break;
                int handle = t[i + 2] | (t[i + 3] << 8);

                // as strings comecam logo depois da parte formatada e terminam
                // em dois zeros seguidos
                int s = i + tam;
                List<string> strings = new List<string>();
                int ini = s;
                while (s + 1 < t.Length && !(t[s] == 0 && t[s + 1] == 0))
                {
                    if (t[s] == 0) { strings.Add(Encoding.ASCII.GetString(t, ini, s - ini)); ini = s + 1; }
                    s++;
                }
                if (s > ini) strings.Add(Encoding.ASCII.GetString(t, ini, s - ini));
                int proximo = s + 2;

                if (tipo == 16 && tam >= 0x07)
                    arranjos[handle] = t[i + 0x05] == 0x03;
                else if (tipo == 17 && tam >= 0x15)
                {
                    SlotDeMemoria m = new SlotDeMemoria();
                    int arranjo = t[i + 0x04] | (t[i + 0x05] << 8);
                    m.Localizador = Texto(strings, t[i + 0x10]);
                    m.Banco = Texto(strings, t[i + 0x11]);
                    m.Bytes = Tamanho(t, i, tam);
                    if (tam >= 0x1B)
                    {
                        m.Fabricante = Texto(strings, t[i + 0x17]);
                        m.Peca = Texto(strings, t[i + 0x1A]);
                    }
                    brutos.Add(new KeyValuePair<int, SlotDeMemoria>(arranjo, m));
                }
                else if (tipo == 127) break;   // fim da tabela

                i = proximo;
            }

            foreach (KeyValuePair<int, SlotDeMemoria> b in brutos)
            {
                bool sistema;
                if (arranjos.Count > 0 && arranjos.TryGetValue(b.Key, out sistema) && !sistema) continue;
                saida.Add(b.Value);
            }
            return saida;
        }

        private static string Texto(List<string> strings, int indice)
        {
            if (indice <= 0 || indice > strings.Count) return null;
            string s = strings[indice - 1].Trim();
            return s.Length == 0 ? null : s;
        }

        /// <summary>
        /// Tamanho em bytes. 0x0C e o campo curto: zero e "nada instalado",
        /// 0xFFFF "desconhecido", 0x7FFF "olhe o estendido" (0x1C, em MB), e o
        /// bit 15 troca a unidade de MB para KB.
        /// </summary>
        private static ulong Tamanho(byte[] t, int i, int tam)
        {
            int curto = t[i + 0x0C] | (t[i + 0x0D] << 8);
            if (curto == 0 || curto == 0xFFFF) return 0;
            if (curto == 0x7FFF && tam >= 0x20)
                return (ulong)(BitConverter.ToUInt32(t, i + 0x1C) & 0x7FFFFFFF) * 1024UL * 1024UL;
            if ((curto & 0x8000) != 0) return (ulong)(curto & 0x7FFF) * 1024UL;
            return (ulong)curto * 1024UL * 1024UL;
        }

        /// <summary>
        /// Da a cada slot um nome curto, canal e numero, e poe em ordem.
        ///
        /// Dois jeitos aparecem nas placas. Ou o nome ja vem pronto no Device
        /// Locator ("DIMM_A1", "DDR4_B2", "A1"), ou vem repartido: o canal no
        /// Bank Locator ("P0 CHANNEL A") ou no proprio localizador
        /// ("ChannelA-DIMM0"), e o numero em "DIMM 0". Contado a partir de zero,
        /// soma-se um, que e como a placa imprime.
        ///
        /// Falso quando algum slot nao se deixa nomear ou dois saem com o mesmo
        /// nome: um desenho com o nome errado e pior que o desenho antigo.
        /// </summary>
        internal static bool Nomear(List<SlotDeMemoria> slots)
        {
            if (slots == null || slots.Count == 0 || slots.Count > 8) return false;

            char[] canal = new char[slots.Count];
            int[] numero = new int[slots.Count];
            bool doZero = false;

            for (int k = 0; k < slots.Count; k++)
            {
                string loc = slots[k].Localizador ?? "", banco = slots[k].Banco ?? "";

                Match pronto = Regex.Match(loc, @"(?:^|[^A-Za-z])([A-H])[\s_-]?([1-9])(?!\d)");
                if (pronto.Success)
                {
                    canal[k] = char.ToUpperInvariant(pronto.Groups[1].Value[0]);
                    numero[k] = int.Parse(pronto.Groups[2].Value, CultureInfo.InvariantCulture);
                    continue;
                }

                Match c = Regex.Match(loc + " " + banco, @"(?i)CHANNEL\s*[_-]?\s*([A-H])(?![A-Za-z])");
                if (!c.Success) c = Regex.Match(loc + " " + banco, @"(?i)(?:^|[^A-Za-z])CH\s*[_-]?\s*([A-H])(?![A-Za-z])");
                Match d = Regex.Match(loc, @"(?i)DIMM\s*[_-]?\s*(\d)(?!\d)");
                if (!c.Success || !d.Success) return false;

                canal[k] = char.ToUpperInvariant(c.Groups[1].Value[0]);
                numero[k] = int.Parse(d.Groups[1].Value, CultureInfo.InvariantCulture);
                if (numero[k] == 0) doZero = true;
            }

            HashSet<string> vistos = new HashSet<string>(StringComparer.Ordinal);
            for (int k = 0; k < slots.Count; k++)
            {
                string nome = canal[k].ToString() + (doZero ? numero[k] + 1 : numero[k]).ToString(CultureInfo.InvariantCulture);
                if (!vistos.Add(nome)) return false;
                slots[k].Nome = nome;
            }

            // Ordem pelo nome, canal e depois numero: e a da maioria das placas
            // da esquerda para a direita.
            slots.Sort(delegate(SlotDeMemoria a, SlotDeMemoria b) { return string.CompareOrdinal(a.Nome, b.Nome); });
            return true;
        }
    }
}
