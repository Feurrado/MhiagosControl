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
    public partial class TrayContext : ApplicationContext, ISettingsData, IPanelStatusData, IReadingStatusData, IControleDoWidget
    {
        // ---------------- menu ----------------

        private void BuildMenu()
        {
            ContextMenuStrip menu = MenuTema.Novo();

            _miProfiles = MenuTema.Submenu(T.TrayProfiles);
            menu.Items.Add(_miProfiles);
            RebuildProfileMenu();

            menu.Items.Add(MenuTema.Item(T.TrayConfigure, new EventHandler(OnConfig)));

            _miWidget = MenuTema.Item(T.TrayBatteryWidget, new EventHandler(OnToggleWidget));
            _miWidget.Checked = _cfgWidget.Ativo;
            menu.Items.Add(_miWidget);
            MenuTema.Separador(menu.Items);

            _miPause = MenuTema.Item(T.TrayPause, new EventHandler(OnPause));
            menu.Items.Add(_miPause);

            _miAutostart = MenuTema.Item(T.TrayAutostart, new EventHandler(OnToggleAutostart));
            _miAutostart.Checked = Autostart.IsEnabled();
            menu.Items.Add(_miAutostart);

            menu.Items.Add(MenuTema.Item(T.TrayOpenData, new EventHandler(OnOpenData)));
            MenuTema.Separador(menu.Items);
            menu.Items.Add(MenuTema.Item(T.TrayExit, new EventHandler(OnExit)));

            _icon.ContextMenuStrip = menu;
        }

        private void RebuildProfileMenu()
        {
            _miProfiles.DropDownItems.Clear();
            string padrao = _cfg.DefaultProfile.Name;
            foreach (Profile p in _cfg.Profiles)
            {
                string texto = p.Name + (string.Equals(p.Name, padrao, StringComparison.Ordinal)
                    ? "  ·  " + T.DefaultBadge : "");
                ToolStripMenuItem mi = MenuTema.Opcao(texto, false, new EventHandler(OnPickProfile));
                mi.Tag = p;
                mi.Checked = (p.Name == _cfg.ActiveName);
                _miProfiles.DropDownItems.Add(mi);
            }
        }

        private void OnPickProfile(object sender, EventArgs e)
        {
            ToolStripMenuItem mi = sender as ToolStripMenuItem;
            if (mi == null) return;
            Profile p = mi.Tag as Profile;
            if (p == null) return;

            string erro;
            bool salvo = ProfileActivation.TryActivate(_cfg, p.Name, out erro);
            if (!salvo)
            {
                RebuildProfileMenu();
                NotificarComAcao(T.AppName, T.SaveFailed(erro), delegate { OnPickProfile(sender, e); });
                return;
            }

            RegistrarEscolhaManual();

            Publicar();
            RebuildProfileMenu();
            Log.Write("perfil ativo: " + p.Name);
        }

        private void RegistrarEscolhaManual()
        {
            _ativacaoDeJogoPendente = false;
            string jogo = _cfg.GameProfiles ? Rtss.JogoAtual : null;
            string vinculo = string.IsNullOrEmpty(jogo) ? null : _cfg.PerfilDoJogo(jogo);
            if (string.IsNullOrEmpty(vinculo) || !_cfg.NameExists(vinculo)) return;
            // Inclui a escolha feita antes do primeiro tick ou depois de uma
            // ativacao que falhou. O retorno ao padrao pertence ao jogo.
            _jogoVisto = jogo;
            _vinculoVisto = vinculo;
            _perfilDeJogoAplicado = true;
        }

        // ---------------- perfil por jogo ----------------

        private System.Windows.Forms.Timer _relogioDoJogo;
        private string _jogoVisto;            // o executavel do ciclo anterior
        private string _vinculoVisto;
        private bool _ativacaoDeJogoPendente;
        private bool _perfilDeJogoAplicado;
    }
}
