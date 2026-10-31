using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace MhiagosControl
{
    public sealed partial class WidgetDeBateria : Form
    {
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_sobre) { _sobre = true; Renderizar(); }
            if (!_pressionado && _casa.Sobre(_casa.Zona(e.Location))) Renderizar();

            if (_pressionado && !_cfg.Travado && !_noBotaoDaCasa)
            {
                Point agora = Cursor.Position;
                int dx = agora.X - _inicioDoArrasto.X, dy = agora.Y - _inicioDoArrasto.Y;
                if (!_arrastando && (Math.Abs(dx) > Arrasto || Math.Abs(dy) > Arrasto))
                {
                    _arrastando = true;
                    _dica.Hide(this);
                }
                if (_arrastando)
                {
                    Point alvo = new Point(_origemDoArrasto.X + dx, _origemDoArrasto.Y + dy);
                    Rectangle area = Screen.FromPoint(agora).WorkingArea;
                    Location = Encaixar(alvo, Size, area, (int)(12 * _escala));
                }
                return;
            }

            AtualizarDica(e.Location);
        }

        /// <summary>
        /// Outra janela tomou o mouse no meio do arrasto (tecla Windows, UAC,
        /// Alt+Tab): o MouseUp nao vem mais, e sem isso o widget seguiria o
        /// cursor so de passar por cima.
        /// </summary>
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            // botao ja solto: e a soltura normal, o MouseUp vem logo depois
            if (Capture || !_pressionado || (Control.MouseButtons & MouseButtons.Left) == 0) return;
            _pressionado = false;
            if (_arrastando)
            {
                _arrastando = false;
                ManterNaTela();
                GuardarPosicao();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Right) { MostrarMenu(e.Location); return; }
            if (e.Button != MouseButtons.Left || !_pressionado) return;
            _pressionado = false;
            Capture = false;
            if (_arrastando)
            {
                _arrastando = false;
                ManterNaTela();
                GuardarPosicao();
                return;
            }
            // os botoes da casa mandam o comando; o resto de um item da casa abre a pagina Casa
            int zc = _casa.Zona(e.Location);
            if (zc >= 0) { _casa.Clicar(zc); return; }
            if (BateriasPedidas != null) BateriasPedidas();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_arrastando) return;
            _sobre = false;
            _casa.Sobre(-1);
            _zonaDaDica = -1;
            _dica.SetToolTip(this, null);
            Renderizar();
        }

        /// <summary>Nome, carga e a previsao do aparelho sob o ponteiro.</summary>
        private void AtualizarDica(Point p)
        {
            int zc = _casa.Zona(p);
            if (zc >= 0)
            {
                // zonas da casa: 1000 + a zona dela, para nao cruzar com os perifericos
                if (1000 + zc == _zonaDaDica) return;
                _zonaDaDica = 1000 + zc;
                _dica.SetToolTip(this, _casa.Dica(zc));
                return;
            }
            RectangleF[] zonas = PintorDoWidget.Zonas(_cfg.Layout, _presenca, PintorDoWidget.Larguras(_cfg.Layout, _itens), Medida(), _escala);
            int zona = -1;
            for (int i = 0; i < zonas.Length; i++) if (zonas[i].Contains(p)) { zona = i; break; }
            if (zona == _zonaDaDica) return;
            _zonaDaDica = zona;
            if (zona < 0) { _dica.SetToolTip(this, T.OpenBatteriesPage); return; }
            Periferico it = _itens[zona];
            string texto = it.Nome + "  " + PintorDoWidget.Valor(it);
            string nota = _notas[zona];
            if (!string.IsNullOrEmpty(nota)) texto += "\n" + nota;
            _dica.SetToolTip(this, texto + "\n" + T.OpenBatteriesPage);
        }

        // ---------------- menu ----------------

        private void MostrarMenu(Point onde)
        {
            ContextMenu menu = new ContextMenu();
            menu.MenuItems.Add(new MenuItem(T.WidgetOpenBatteries, delegate { if (BateriasPedidas != null) BateriasPedidas(); }));
            menu.MenuItems.Add("-");

            MenuItem layout = new MenuItem(T.WidgetLayout);
            layout.MenuItems.Add(Opcao(T.WidgetRings, _cfg.Layout == LayoutDoWidget.Aneis, delegate { TrocarLayout(LayoutDoWidget.Aneis); }));
            layout.MenuItems.Add(Opcao(T.WidgetList, _cfg.Layout == LayoutDoWidget.Lista, delegate { TrocarLayout(LayoutDoWidget.Lista); }));
            menu.MenuItems.Add(layout);

            MenuItem vidro = new MenuItem(T.WidgetGlass);
            vidro.MenuItems.Add(Opcao(T.WidgetSharp, _cfg.Nitido, delegate { TrocarVidro(true, false); }));
            vidro.MenuItems.Add(Opcao(T.WidgetClear, !_cfg.Nitido && !_cfg.Fosco, delegate { TrocarVidro(false, false); }));
            vidro.MenuItems.Add(Opcao(T.WidgetFrosted, !_cfg.Nitido && _cfg.Fosco, delegate { TrocarVidro(false, true); }));
            vidro.MenuItems.Add("-");
            vidro.MenuItems.Add(Opcao(T.WidgetTintAuto, _cfg.Cor == CorDoVidro.Automatica, delegate { TrocarCor(CorDoVidro.Automatica); }));
            vidro.MenuItems.Add(Opcao(T.WidgetTintDark, _cfg.Cor == CorDoVidro.Escura, delegate { TrocarCor(CorDoVidro.Escura); }));
            vidro.MenuItems.Add(Opcao(T.WidgetTintLight, _cfg.Cor == CorDoVidro.Clara, delegate { TrocarCor(CorDoVidro.Clara); }));
            menu.MenuItems.Add(vidro);

            MenuItem frente = new MenuItem(T.WidgetOnTop, delegate { TrocarFrente(!_cfg.NaFrente); });
            frente.Checked = _cfg.NaFrente;
            menu.MenuItems.Add(frente);
            MenuItem trava = new MenuItem(T.WidgetLock, delegate { _cfg.Travado = !_cfg.Travado; _cfg.Salvar(); });
            trava.Checked = _cfg.Travado;
            menu.MenuItems.Add(trava);
            MenuItem casa = new MenuItem(T.CasaWidgetOpcao, delegate { TrocarCasa(!_cfg.Casa); });
            casa.Checked = _cfg.Casa;
            menu.MenuItems.Add(casa);

            menu.MenuItems.Add("-");
            menu.MenuItems.Add(new MenuItem(T.WidgetHide, delegate { if (OcultarPedido != null) OcultarPedido(); }));

            // Menu nativo numa janela que nunca fica ativa: sem o primeiro plano
            // ele nao fecha ao clicar fora (comportamento documentado do
            // TrackPopupMenu). A janela volta ao fundo pelo WM_WINDOWPOSCHANGING.
            SetForegroundWindow(Handle);
            _dica.Hide(this);
            // O clique numa opcao chega depois, como WM_COMMAND: descartar o
            // menu logo apos o Show perderia a acao. Fica ate o proximo.
            if (_menu != null) _menu.Dispose();
            _menu = menu;
            menu.Show(this, onde);
        }

        private static MenuItem Opcao(string texto, bool marcado, EventHandler acao)
        {
            MenuItem mi = new MenuItem(texto, acao);
            mi.RadioCheck = true;
            mi.Checked = marcado;
            return mi;
        }

        private void TrocarLayout(LayoutDoWidget layout)
        {
            if (_cfg.Layout == layout) return;
            _cfg.Layout = layout;
            _cfg.Salvar();
            _zonaDaDica = -1;
            Renderizar();
            ManterNaTela();
        }
    }
}
