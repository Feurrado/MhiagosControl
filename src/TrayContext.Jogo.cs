using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using LibreHardwareMonitor.Hardware;

namespace MhiagosControl
{
    public partial class TrayContext : ApplicationContext, ISettingsData, IPanelStatusData, IReadingStatusData
    {
        /// <summary>
        /// Vigia qual jogo esta em primeiro plano e troca o perfil conforme o
        /// mapa.
        ///
        /// Um segundo e de sobra: abrir um jogo leva dezenas deles, e um
        /// intervalo mais curto so gastaria ciclos comparando duas strings
        /// iguais. Na THREAD DA INTERFACE de proposito - trocar de perfil mexe no
        /// menu da bandeja e na configuracao em disco, e faze-lo a partir da
        /// thread do mostrador seria corrida com a janela aberta.
        /// </summary>
        private void IniciarVigiaDeJogo()
        {
            _relogioDoJogo = new System.Windows.Forms.Timer();
            _relogioDoJogo.Interval = 1000;
            _relogioDoJogo.Tick += new EventHandler(OnVigiaDeJogo);
            _relogioDoJogo.Start();
        }

        private void OnVigiaDeJogo(object sender, EventArgs e)
        {
            try
            {
                // A automacao espera o fim da edicao e reavalia tambem o vinculo.
                if (_janelaAberta) return;

                string agora = _cfg.GameProfiles ? Rtss.JogoAtual : null;
                string alvo = string.IsNullOrEmpty(agora) ? null : _cfg.PerfilDoJogo(agora);
                if (string.IsNullOrEmpty(alvo) || !_cfg.NameExists(alvo)) alvo = null;
                if (!string.Equals(agora, _jogoVisto, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(alvo, _vinculoVisto, StringComparison.Ordinal))
                {
                    _jogoVisto = agora;
                    _vinculoVisto = alvo;
                    _ativacaoDeJogoPendente = alvo != null;
                }

                if (alvo == null)
                {
                    if (_perfilDeJogoAplicado) VoltarAoPadrao("fim do jogo vinculado");
                    return;
                }

                // Uma falha de disco fica pendente para o proximo tick. Depois
                // do sucesso, nao desfazer uma escolha manual no mesmo jogo.
                if (_ativacaoDeJogoPendente &&
                    (alvo == _cfg.ActiveName || TrocarPorJogo(alvo, "jogo " + agora)))
                {
                    _perfilDeJogoAplicado = true;
                    _ativacaoDeJogoPendente = false;
                }
            }
            catch (Exception ex) { Log.Error("vigia de perfil por jogo", ex); }
        }

        private void VoltarAoPadrao(string motivo)
        {
            string padrao = _cfg.DefaultProfile.Name;
            bool concluido = string.Equals(padrao, _cfg.ActiveName, StringComparison.Ordinal) ||
                             TrocarPorJogo(padrao, motivo);
            if (concluido) _perfilDeJogoAplicado = false;
        }

        private bool TrocarPorJogo(string nome, string motivo)
        {
            string erro;
            if (!ProfileActivation.TryActivate(_cfg, nome, out erro))
            {
                Log.Write("perfil por jogo nao persistiu: " + erro);
                return false;
            }
            Publicar();
            RebuildProfileMenu();

            // Registrado SEMPRE. O aplicativo trocou o que esta na peca sem
            // ninguem pedir; quem for procurar por que o mostrador mudou tem de
            // achar a resposta escrita.
            Log.Write("perfil por jogo: " + nome + " (" + motivo + ")");
            return true;
        }
    }
}
