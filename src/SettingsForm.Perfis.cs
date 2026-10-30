using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using LibreHardwareMonitor.Hardware;

namespace MhiagosControl
{
    public partial class SettingsForm : Form
    {
        // ---------------- pagina: Perfis ----------------

        /// <summary>
        /// Perfis: a lista de um lado, o que o perfil selecionado poe no
        /// mostrador do outro.
        ///
        /// A previa vem para ca porque a pergunta que se faz nesta pagina e
        /// "qual deles eu quero agora", e ela nao se responde com uma lista de
        /// nomes. Antes era preciso selecionar um perfil, ir ate a pagina de
        /// paineis para ver o que ele mostra, voltar e repetir com o proximo.
        /// </summary>
        private Control BuildPagePerfis()
        {
            Panel page = new Pagina();
            ((Pagina)page).RolarNaVertical();
            // fundo opaco vem da propria Pagina

            // Duas colunas: a lista, com os seis botoes de perfil embaixo e o
            // Rodizio logo abaixo dela, ja que ele vale para o perfil
            // selecionado; do outro lado a previa, com sensores, Padrao e
            // Aplicar ao lado da imagem, e o Perfil por jogo embaixo.
            Card c = new Card();
            c.Title = T.SavedProfiles;
            _profilesListCard = c;
            c.SetBounds(0, 0, Ui.S(330), Ui.S(400));
            page.Controls.Add(c);

            _profileList = new ProfileList();
            _profileList.SetBounds(Ui.S(12), Ui.S(44), Ui.S(306), Ui.S(300));
            _profileList.Resolve = NomeDoSensor;
            _profileList.ActiveName = _cfg.ActiveName;
            _profileList.DefaultName = _cfg.DefaultProfile.Name;
            _profileList.SelectionChanged += new EventHandler(OnProfileSelected);
            _profileList.ItemActivated += delegate { AplicarPerfil(); };
            c.Controls.Add(_profileList);

            _botoesDePerfil.Clear();
            BotaoDePerfil(T.New, new EventHandler(OnNewProfile));
            BotaoDePerfil(T.Rename, new EventHandler(OnRenameProfile));
            BotaoDePerfil(T.Duplicate, new EventHandler(OnDuplicateProfile));
            BotaoDePerfil(T.Export, new EventHandler(OnExportProfile));
            BotaoDePerfil(T.Import, new EventHandler(OnImportProfile));
            BotaoDePerfil(T.Delete, new EventHandler(OnDeleteProfile)).Danger = true;

            Card cv = new Card();
            cv.Title = T.ProfilePreview;
            _profilesPreviewCard = cv;
            cv.SetBounds(Ui.S(346), 0, Ui.S(410), Ui.S(400));
            page.Controls.Add(cv);

            _profilePreview = new PanelPreview();
            _profilePreview.SetBounds(Ui.S(12), Ui.S(44), Ui.S(386), Ui.S(250));
            cv.Controls.Add(_profilePreview);

            _profileSensor1 = new FlatBtn();
            _profileSensor1.SetBounds(Ui.S(12), Ui.S(306), Ui.S(189), Ui.S(34));
            _profileSensor1.Click += delegate { TrocarSensorDoPerfil(1); };
            cv.Controls.Add(_profileSensor1);

            _profileSensor2 = new FlatBtn();
            _profileSensor2.SetBounds(Ui.S(209), Ui.S(306), Ui.S(189), Ui.S(34));
            _profileSensor2.Click += delegate { TrocarSensorDoPerfil(2); };
            cv.Controls.Add(_profileSensor2);

            _btnDefault = new FlatBtn();
            _btnDefault.Text = T.SetAsDefault;
            _btnDefault.Size = new Size(Ui.S(160), Ui.S(34));
            _btnDefault.Click += delegate { DefinirPerfilPadrao(); };
            cv.Controls.Add(_btnDefault);

            _btnApply = new FlatBtn();
            _btnApply.Text = T.ApplyProfile;
            _btnApply.Primary = true;
            _btnApply.Size = new Size(Ui.S(160), Ui.S(34));
            _btnApply.Click += delegate { AplicarPerfil(); };
            cv.Controls.Add(_btnApply);


            // A automacao por jogo vem antes do rodizio: ela e uma relacao entre
            // o perfil selecionado acima e um aplicativo real, e deixa-la no fim
            // escondia a acao principal atras de uma frase com apenas a contagem.
            _cardGame = new Card();
            _cardGame.Title = T.GameProfilesCard;
            _cardGame.SetBounds(0, Ui.S(512), Ui.S(756), Ui.S(400));
            page.Controls.Add(_cardGame);

            _tgJogo = new Toggle();
            _tgJogo.Label = T.GameProfilesOn;
            _tgJogo.SetBounds(Ui.S(16), Ui.S(48), Ui.S(560), Ui.S(26));
            _tgJogo.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _tgJogo.CheckedChanged += new EventHandler(OnToggleJogo);
            _cardGame.Controls.Add(_tgJogo);

            Label atual = MakeLabel(T.CurrentGame, 16, 84, Ui.FontSmall);
            atual.ForeColor = Ui.Muted;
            _cardGame.Controls.Add(atual);

            _gameIcon = new GameIconView();
            // O centro coincide com o ícone das linhas de vínculos abaixo.
            _gameIcon.SetBounds(Ui.S(24), Ui.S(108), Ui.S(52), Ui.S(52));
            _cardGame.Controls.Add(_gameIcon);

            _lbJogoNome = MakeLabel(T.NoGameToBind, 82, 106, Ui.FontMed);
            _lbJogoNome.Size = new Size(Ui.S(400), Ui.S(26));
            _lbJogoNome.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _cardGame.Controls.Add(_lbJogoNome);

            _lbJogoAtual = MakeLabel("", 82, 132, Ui.FontSmall);
            _lbJogoAtual.Size = new Size(Ui.S(400), Ui.S(22));
            _lbJogoAtual.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _lbJogoAtual.ForeColor = Ui.AccentText;
            _cardGame.Controls.Add(_lbJogoAtual);

            _btVincular = new FlatBtn();
            _btVincular.Text = T.BindGame;
            _btVincular.Primary = true;
            _btVincular.SetBounds(Ui.S(510), Ui.S(106), Ui.S(230), Ui.S(34));
            _btVincular.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btVincular.Click += delegate { VincularJogo(); };
            _cardGame.Controls.Add(_btVincular);

            _btDesvincular = new FlatBtn();
            _btDesvincular.Text = T.UnbindGame;
            _btDesvincular.SetBounds(Ui.S(510), Ui.S(146), Ui.S(230), Ui.S(30));
            _btDesvincular.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btDesvincular.Click += delegate { DesvincularJogo(); };
            _cardGame.Controls.Add(_btDesvincular);

            _lbJogoNota = MakeLabel(T.GameProfilesNote, 16, 177, Ui.FontSmall);
            _lbJogoNota.Size = new Size(Ui.S(724), Ui.S(38));
            _lbJogoNota.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _lbJogoNota.ForeColor = Ui.Faint;
            _cardGame.Controls.Add(_lbJogoNota);

            FlatBtn browseGame = MakeSideButton(T.GameBrowse, 16, 220, 240, delegate { BrowseGame(); });
            _gameFileButton = browseGame;
            _cardGame.Controls.Add(browseGame);
            Label browseNote = MakeLabel(T.GameFileNote, 16, 260, Ui.FontSmall);
            _gameFileNote = browseNote;
            browseNote.Size = new Size(Ui.S(724), Ui.S(50));
            browseNote.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            browseNote.ForeColor = Ui.Muted;
            _cardGame.Controls.Add(browseNote);

            Label vinculos = MakeLabel(T.GameBindings, 16, 319, Ui.FontMed);
            _gameBindingsHeading = vinculos;
            _cardGame.Controls.Add(vinculos);

            _gameBindings = new GameBindingList();
            _gameBindings.EmptyText = T.NoGameBindings;
            _gameBindings.EmptyActionText = T.GameBrowse;
            _gameBindings.EmptyActionRequested += delegate { BrowseGame(); };
            _gameBindings.SetBounds(Ui.S(16), Ui.S(346), Ui.S(724), Ui.S(138));
            _gameBindings.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _gameBindings.RemoveRequested += delegate(string key) { RemoverVinculo(key); };
            _cardGame.Controls.Add(_gameBindings);

            // O rodizio continua junto dos perfis, mas depois da automacao mais
            // frequente e visualmente mais rica.
            _cardRotation = new Card();
            _cardRotation.Title = T.Rotation;
            _cardRotation.SetBounds(0, Ui.S(924), Ui.S(756), Ui.S(150));
            page.Controls.Add(_cardRotation);

            _tgRotate = new Toggle();
            _tgRotate.Label = T.IncludeInRotation;
            _tgRotate.SetBounds(Ui.S(16), Ui.S(48), Ui.S(460), Ui.S(26));
            _tgRotate.CheckedChanged += new EventHandler(OnToggleRotate);
            _cardRotation.Controls.Add(_tgRotate);

            _rotSecs = new NumberBox();
            _rotSecs.Minimum = 2; _rotSecs.Maximum = 999;
            _rotSecs.Value = _cfg.RotateSeconds > 0 ? _cfg.RotateSeconds : 20;
            _rotSecs.SetBounds(Ui.S(16), Ui.S(84), Ui.S(110), Ui.S(32));
            _rotSecs.ValueChanged += new EventHandler(OnRotateSeconds);
            _cardRotation.Controls.Add(_rotSecs);

            _rotNote = MakeLabel("", 134, 84, Ui.FontSmall);
            _rotNote.Size = new Size(Ui.S(600), Ui.S(32));
            _rotNote.TextAlign = ContentAlignment.MiddleLeft;
            _rotNote.ForeColor = Ui.Muted;
            _cardRotation.Controls.Add(_rotNote);

            _profilesNote = MakeLabel(T.ProfilesNote, 2, 1086, Ui.FontSmall);
            _profilesNote.Size = new Size(Ui.S(750), Ui.S(40));
            _profilesNote.ForeColor = Ui.Muted;
            page.Controls.Add(_profilesNote);

            AtualizarRodizio();
            AtualizarJogo();
            return page;
        }

        /// <summary>Os seis botoes de perfil, em grade de tres por dois embaixo da lista.</summary>
        private readonly List<FlatBtn> _botoesDePerfil = new List<FlatBtn>();

        private FlatBtn BotaoDePerfil(string texto, EventHandler h)
        {
            FlatBtn b = new FlatBtn();
            b.Text = texto;
            b.Size = new Size(Ui.S(100), Ui.S(32));
            b.Click += h;
            _profilesListCard.Controls.Add(b);
            _botoesDePerfil.Add(b);
            return b;
        }

        /// <summary>Edita o sensor do perfil sem obrigar a ida ate Paineis.</summary>
        private void TrocarSensorDoPerfil(int painel)
        {
            Profile p = _profileList != null && _profileList.Selected != null
                      ? _profileList.Selected : _current;
            if (p == null) return;

            string atual = painel == 1 ? p.Panel1Id : p.Panel2Id;
            string titulo = painel == 1 ? T.PickSensor1 : T.PickSensor2;
            using (SensorDialog d = new SensorDialog(Clone(_sensors), atual, titulo))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                if (painel == 1) p.Panel1Id = d.SelectedId;
                else
                {
                    p.Panel2Id = d.SelectedId;
                    SensorEntry s = Achar(d.SelectedId);
                    p.Percent = s == null || s.Type != SensorType.Power;
                }
            }

            _dirty = true;
            LoadFromProfile();
            _profileList.Invalidate();
            AtualizarPreviaDoPerfil();
        }

        /// <summary>
        /// Marca o perfil selecionado como participante do rodizio.
        ///
        /// A marca e do perfil, e o periodo e da maquina: dois perfis nao
        /// podem discordar sobre de quanto em quanto tempo a roda gira.
        /// </summary>
        private void OnToggleRotate(object sender, EventArgs e)
        {
            if (_loading) return;
            Profile p = _profileList != null ? _profileList.Selected : _current;
            if (p == null) return;
            p.Rotate = _tgRotate.Checked;
            _cfg.RotateSeconds = _rotSecs.Value;
            _dirty = true;
            _profileList.Invalidate();
            AtualizarRodizio();
        }

        private void OnRotateSeconds(object sender, EventArgs e)
        {
            if (_loading) return;
            _cfg.RotateSeconds = _rotSecs.Value;
            _dirty = true;
            AtualizarRodizio();
        }

        // ---------------- perfil por jogo ----------------

        private Toggle _tgJogo;
        private Label _lbJogoAtual, _lbJogoNome, _lbJogoNota;
        private FlatBtn _btVincular, _btDesvincular;
        private GameIconView _gameIcon;
        private GameBindingList _gameBindings;
        private readonly Dictionary<string, GameIdentityInfo> _gameIdentityCache =
            new Dictionary<string, GameIdentityInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _gameIdentityRetry =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private string _gameBindingsSignature = null;

        private void OnToggleJogo(object sender, EventArgs e)
        {
            if (_loading) return;
            _cfg.GameProfiles = _tgJogo.Checked;
            _dirty = true;
            AtualizarJogo();
        }

        /// <summary>
        /// O jogo detectado agora e o vinculo dele, se houver.
        ///
        /// Diz na cara quando o RTSS nao esta publicando. Um interruptor ligado
        /// que nunca age e a pior forma de recurso quebrado: parece configurado,
        /// nao reclama, e so nao funciona.
        /// </summary>
        private void AtualizarJogo()
        {
            if (_lbJogoAtual == null) return;

            _loading = true;
            _tgJogo.Checked = _cfg.GameProfiles;
            _loading = false;

            string jogo = Rtss.JogoAtual;
            bool temJogo = !string.IsNullOrEmpty(jogo);
            bool temRtss = Rtss.Presente();

            GameIdentityInfo identity = temJogo ? IdentidadeDoJogo(jogo, Rtss.JogoAtualPid) : null;
            string casado = temJogo ? _cfg.PerfilDoJogo(jogo) : null;
            Profile selecionado = _profileList != null && _profileList.Selected != null
                ? _profileList.Selected : _current;

            _lbJogoNome.Text = !temRtss ? T.RtssMissing
                              : (identity != null ? identity.DisplayName : T.NoGameToBind);
            _lbJogoNome.ForeColor = temRtss ? Ui.Text : Ui.Warn;
            _lbJogoAtual.Text = !temJogo ? T.GamesBoundCount(_cfg.GameKeys.Count)
                               : (!string.IsNullOrEmpty(casado)
                                  ? T.BoundProfile(casado)
                                  : T.SelectedProfile(selecionado != null ? selecionado.Name : "—"));
            _lbJogoAtual.ForeColor = !string.IsNullOrEmpty(casado) ? Ui.AccentText : Ui.Muted;
            _gameIcon.GameIcon = identity != null ? identity.Icon : null;

            _btVincular.Enabled = _cfg.GameProfiles && temJogo;
            _btVincular.Text = selecionado != null ? T.BindGameTo(selecionado.Name) : T.BindGame;
            _btDesvincular.Enabled = _cfg.GameProfiles && temJogo &&
                                     !string.IsNullOrEmpty(casado);
            _tgJogo.Enabled = temRtss;

            AtualizarListaDeJogos(jogo);
        }

        private GameIdentityInfo IdentidadeDoJogo(string key, int pid)
        {
            if (string.IsNullOrEmpty(key)) return null;
            GameIdentityInfo info;
            if (_gameIdentityCache.TryGetValue(key, out info))
            {
                DateTime retry;
                bool waiting = _gameIdentityRetry.TryGetValue(key, out retry) &&
                               DateTime.UtcNow < retry;
                // Uma identidade completa e imutavel durante a sessao. Quando
                // falta o icone, tenta novamente com baixa frequencia em vez de
                // consultar processo, versao e Registro a cada tick da tela.
                if (info.Icon != null || waiting || pid <= 0) return info;
            }

            info = GameIdentity.Resolve(key, pid, _cfg.NomeDoJogo(key), _cfg.CaminhoDoJogo(key));
            _gameIdentityCache[key] = info;
            _gameIdentityRetry[key] = DateTime.UtcNow.AddSeconds(info.Icon != null ? 300 : 20);
            // Guarda no rascunho SEM sujar a janela. Descobrir o nome e o icone de
            // um jogo ja vinculado e trabalho do tick, nao edicao de ninguem:
            // marcar pendencia aqui acendia o botao Salvar e fazia o aviso de
            // "alteracoes nao salvas" aparecer ao fechar uma janela em que a
            // pessoa nao tocou. A identidade acompanha a proxima gravacao de
            // verdade e, ate la, sai do cache a cada abertura.
            if (_cfg.PerfilDoJogo(key) != null)
                _cfg.IdentificarJogo(key, info.DisplayName, info.ExecutablePath);
            return info;
        }

        private void AtualizarListaDeJogos(string atual)
        {
            if (_gameBindings == null) return;
            List<GameBindingView> items = new List<GameBindingView>();
            System.Text.StringBuilder signature = new System.Text.StringBuilder();
            for (int i = 0; i < _cfg.GameKeys.Count && i < _cfg.GameProfileNames.Count; i++)
            {
                string key = _cfg.GameKeys[i];
                GameIdentityInfo identity = IdentidadeDoJogo(key,
                    string.Equals(key, atual, StringComparison.OrdinalIgnoreCase) ? Rtss.JogoAtualPid : 0);
                GameBindingView item = new GameBindingView();
                item.Key = key;
                item.Name = identity != null ? identity.DisplayName : GameIdentity.Humanize(key);
                item.Profile = _cfg.GameProfileNames[i];
                item.Icon = identity != null ? identity.Icon : null;
                item.Current = string.Equals(key, atual, StringComparison.OrdinalIgnoreCase);
                items.Add(item);
                signature.Append(key).Append('|').Append(item.Name).Append('|')
                         .Append(item.Profile).Append('|').Append(item.Current).Append('|')
                         .Append(item.Icon != null).Append('\n');
            }
            string next = signature.ToString();
            if (next == _gameBindingsSignature) return;
            _gameBindingsSignature = next;
            _gameBindings.SetItems(items);
            ArranjarSecaoDeJogos(items.Count);
        }

        private void ArranjarSecaoDeJogos(int quantidade)
        {
            if (_cardGame == null || _gameBindings == null || _cardRotation == null) return;
            _gameBindings.Height = GameBindingList.PreferredHeight(quantidade);
            _cardGame.Height = _gameBindings.Bottom + Ui.S(16);
            if (_pgPerfis != null && _pgPerfis.Visible) { ArrangeProfiles(); return; }
            _cardRotation.Top = _cardGame.Bottom + Ui.S(12);
            if (_profilesNote != null) _profilesNote.Top = _cardRotation.Bottom + Ui.S(12);
            Pagina pagina = _pgPerfis as Pagina;
            if (pagina != null) pagina.Sincronizar();
        }

        /// <summary>Casa o jogo detectado com o perfil SELECIONADO na lista.</summary>
        private void VincularJogo()
        {
            string jogo = Rtss.JogoAtual;
            if (string.IsNullOrEmpty(jogo)) return;

            Profile p = _profileList != null && _profileList.Selected != null
                      ? _profileList.Selected : _current;
            if (p == null) return;

            GameIdentityInfo identity = IdentidadeDoJogo(jogo, Rtss.JogoAtualPid);
            _cfg.MapearJogo(jogo, p.Name);
            if (identity != null)
                _cfg.IdentificarJogo(jogo, identity.DisplayName, identity.ExecutablePath);
            _gameIdentityCache[jogo] = identity;
            _gameBindingsSignature = null;
            _dirty = true;
            AtualizarJogo();
            Aviso(T.GameBound(identity != null ? identity.DisplayName : GameIdentity.Humanize(jogo), p.Name));
        }

        private void DesvincularJogo()
        {
            string jogo = Rtss.JogoAtual;
            if (string.IsNullOrEmpty(jogo)) return;
            _cfg.DesmapearJogo(jogo);
            _gameBindingsSignature = null;
            _dirty = true;
            AtualizarJogo();
        }

        private void RemoverVinculo(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            _cfg.DesmapearJogo(key);
            _gameBindingsSignature = null;
            _dirty = true;
            AtualizarJogo();
        }

        private void AtualizarRodizio()
        {
            if (_rotNote == null) return;

            Profile p = _profileList != null ? _profileList.Selected : _current;
            _loading = true;
            _tgRotate.Checked = p != null && p.Rotate;
            _loading = false;

            int n = _cfg.Rotation.Count;
            bool ativo = n >= 2 && _cfg.RotateSeconds > 0;
            _rotSecs.Enabled = n >= 2;
            _rotNote.Text = ativo ? T.RotationOn(n) : T.RotationOff;
            _rotNote.ForeColor = ativo ? Ui.Muted : Ui.Faint;
        }

        /// <summary>Nome curto de um sensor, para o resumo de cada perfil.</summary>
        private string NomeDoSensor(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (SensorEntry s in _sensors) if (s.Id == id) return s.Name;
            return null;
        }

        /// <summary>
        /// Torna ativo o perfil selecionado e grava.
        ///
        /// Aplicar e gravar sao o mesmo gesto de proposito: um perfil "ativo"
        /// que nao sobreviveria a fechar a janela seria uma promessa falsa, e a
        /// thread de atualizacao le o mesmo objeto de configuracao - o
        /// mostrador muda no ciclo seguinte.
        /// </summary>
        private void AplicarPerfil()
        {
            Profile p = _profileList != null ? _profileList.Selected : _current;
            if (p == null) return;

            SaveToProfile();
            string anterior = _cfg.ActiveName;
            _cfg.ActiveName = p.Name;
            string erro;
            bool salvo = _session.TrySaveAll(true, out erro);
            if (!salvo)
            {
                // Aplicar significa tambem sobreviver ao fechamento. Sem disco,
                // manter o novo perfil ativo no worker daria uma troca que some
                // no proximo arranque e contradiz o proprio botao.
                _cfg.ActiveName = anterior;
                _saved = false; _dirty = true;
                FalhaAoGravar(erro);
                return;
            }
            _saved = true; _dirty = false;

            _profileList.ActiveName = p.Name;
            _selectionAtLastSave = _current;
            _profileList.Invalidate();
            AtualizarPreviaDoPerfil();
            if (Applied != null) Applied(this, EventArgs.Empty);
            Aviso(T.ApplyProfile + ": " + p.Name);
        }

        /// <summary>Escolhe e persiste o destino usado quando um jogo termina.</summary>
        private void DefinirPerfilPadrao()
        {
            Profile p = _profileList != null ? _profileList.Selected : _current;
            if (p == null) return;

            SaveToProfile();
            string anterior = _cfg.DefaultProfileName;
            _cfg.DefaultProfileName = p.Name;
            string erro;
            if (!_session.TrySaveAll(out erro))
            {
                _cfg.DefaultProfileName = anterior;
                _saved = false; _dirty = true;
                FalhaAoGravar(erro);
                return;
            }

            _saved = true; _dirty = false;
            _profileList.DefaultName = p.Name;
            _selectionAtLastSave = _current;
            _profileList.Invalidate();
            AtualizarPreviaDoPerfil();
            Aviso(T.DefaultProfileSet(p.Name));
            if (Applied != null) Applied(this, EventArgs.Empty);
        }

        private FlatBtn MakeSideButton(string text, int x, int y, int w, EventHandler h)
        {
            FlatBtn b = new FlatBtn();
            b.Text = text;
            b.SetBounds(Ui.S(x), Ui.S(y), Ui.S(w), Ui.S(32));   // medidas de projeto
            b.Click += h;
            return b;
        }

        private void RefreshProfileList()
        {
            if (_profileList == null) return;
            _loading = true;
            _profileList.ActiveName = _session.ActiveProfile.Name;
            _profileList.DefaultName = _cfg.DefaultProfile.Name;
            _profileList.SetItems(_cfg.Profiles, _current);
            _loading = false;
            AtualizarPreviaDoPerfil();
        }

        /// <summary>Espelha o perfil selecionado no mostrador da direita.</summary>
        private void AtualizarPreviaDoPerfil()
        {
            if (_profilePreview == null) return;
            Profile p = _profileList != null && _profileList.Selected != null ? _profileList.Selected : _current;
            if (p == null) return;

            SensorEntry s1 = Achar(p.Panel1Id), s2 = Achar(p.Panel2Id);
            PanelValue v1 = Scaling.Prepare(s1, Scaling.Effective(p.Divisor1, s1), p.Fahrenheit);
            PanelValue v2 = Scaling.Prepare(s2, Scaling.Effective(p.Divisor2, s2), false);

            _profilePreview.Value1 = v1.Value;
            _profilePreview.Value2 = v2.Value;
            _profilePreview.Fahrenheit = p.Fahrenheit;
            _profilePreview.Percent = p.Percent;
            _profilePreview.Invalidate();

            string n1 = NomeDoSensor(p.Panel1Id), n2 = NomeDoSensor(p.Panel2Id);
            _profileSensor1.Text = T.PanelShort(1) + "  ·  " + (n1 ?? T.NoSensorChosen) +
                                   "  (" + (p.Fahrenheit ? "°F" : "°C") + ")";
            _profileSensor2.Text = T.PanelShort(2) + "  ·  " + (n2 ?? T.NoSensorChosen) +
                                   "  (" + (p.Percent ? "%" : "W") + ")";
            AtualizarRodizio();

            string activeName = _session.ActiveProfile.Name;
            _profileList.ActiveName = activeName;
            _profileList.Invalidate();
            bool ativo = string.Equals(p.Name, activeName, StringComparison.Ordinal);
            // como o Padrao ao lado: ja ativo, o botao diz isso, curto, para caber na barra
            _btnApply.Text = ativo ? T.AlreadyActive : T.ApplyProfile;
            _btnApply.Enabled = !ativo;
            _btnApply.Invalidate();

            bool padrao = string.Equals(p.Name, _cfg.DefaultProfile.Name,
                                        StringComparison.Ordinal);
            _btnDefault.Text = padrao ? T.AlreadyDefault : T.SetAsDefault;
            _btnDefault.Enabled = !padrao;
            _btnDefault.Invalidate();
        }

        /// <summary>
        /// Leva o ciclo tambem para a lista bruta.
        ///
        /// Os seletores trabalham sobre copias; a previa de perfil consulta a
        /// lista original, que sem isto ficaria congelada no instante em que a
        /// janela abriu.
        /// </summary>
        private void AtualizarValores(Dictionary<string, float> snap)
        {
            if (snap == null || _sensors == null) return;
            foreach (SensorEntry s in _sensors)
            {
                float v;
                s.Value = snap.TryGetValue(s.Id, out v) && !float.IsNaN(v) && !float.IsInfinity(v)
                    ? (float?)v : null;
            }
        }

        private SensorEntry Achar(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (SensorEntry s in _sensors) if (s.Id == id) return s;
            return null;
        }

        private void OnProfileSelected(object sender, EventArgs e)
        {
            if (_loading) return;
            Profile p = _profileList.Selected;
            if (p == null || p == _current) return;
            SaveToProfile();
            _current = p;
            _nav.Subtitle = _session.ActiveProfile.Name;
            _nav.Invalidate();
            LoadFromProfile();
            AtualizarPreviaDoPerfil();
            AtualizarJogo();
        }

        private void OnNewProfile(object sender, EventArgs e)
        {
            string name = Prompt(T.NewProfileName, T.DefaultProfileName(_cfg.Profiles.Count + 1));
            if (string.IsNullOrEmpty(name)) return;
            if (_cfg.NameExists(name)) { Warn(T.NameTaken); return; }
            SaveToProfile();
            Profile np = new Profile();
            np.Name = name;
            _cfg.Profiles.Add(np);
            _current = np;
            _dirty = true;
            _nav.Subtitle = _session.ActiveProfile.Name; _nav.Invalidate();
            RefreshProfileList(); LoadFromProfile();
        }

        private void OnDuplicateProfile(object sender, EventArgs e)
        {
            string name = Prompt(T.CopyName, _current.Name + T.CopySuffix);
            if (string.IsNullOrEmpty(name)) return;
            if (_cfg.NameExists(name)) { Warn(T.NameTaken); return; }
            SaveToProfile();
            Profile np = _current.Clone();
            np.Name = name;
            _cfg.Profiles.Add(np);
            _current = np;
            _dirty = true;
            _nav.Subtitle = _session.ActiveProfile.Name; _nav.Invalidate();
            RefreshProfileList(); LoadFromProfile();
        }

        /// <summary>
        /// Grava o perfil selecionado num arquivo.
        ///
        /// SaveToProfile antes de tudo porque o perfil em edicao mora nos
        /// controles ate alguem mandar grava-lo: sem isso, exportar logo depois
        /// de trocar um sensor exportaria o valor antigo, e o arquivo sairia
        /// diferente do que esta na tela sem nenhum aviso.
        /// </summary>
        private void OnExportProfile(object sender, EventArgs e)
        {
            SaveToProfile();
            Profile p = _profileList != null && _profileList.Selected != null ? _profileList.Selected : _current;
            if (p == null) return;

            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Title = T.ExportProfile;
                d.Filter = T.ProfileFilter;
                d.AddExtension = true;
                d.DefaultExt = "ini";
                d.FileName = NomeDeArquivo(p.Name) + ".ini";
                if (d.ShowDialog(this) != DialogResult.OK) return;

                string erro;
                if (Config.ExportProfile(p, d.FileName, _cfg.SavePath, out erro)) Aviso(T.Exported(p.Name));
                else Warn(T.ExportFailed(erro), T.TryAgain, delegate { OnExportProfile(null, EventArgs.Empty); });
            }
        }

        private void OnImportProfile(object sender, EventArgs e)
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = T.ImportProfile;
                d.Filter = T.ProfileFilter;
                d.CheckFileExists = true;
                if (d.ShowDialog(this) != DialogResult.OK) return;

                string erro;
                Profile p = Config.ImportProfile(d.FileName, out erro);
                if (p == null) { Warn(T.ImportFailed(erro), T.TryAgain, delegate { OnImportProfile(null, EventArgs.Empty); }); return; }

                SaveToProfile();
                p.Name = _cfg.UniqueName(p.Name);
                _cfg.Profiles.Add(p);
                _current = p;
                _dirty = true;
                _nav.Subtitle = _session.ActiveProfile.Name; _nav.Invalidate();
                RefreshProfileList(); LoadFromProfile();

                // Identificador de sensor nao viaja entre maquinas: ele carrega
                // o modelo do hardware. Um perfil vindo de outro computador
                // entra com o mostrador em branco, e calar sobre isso deixaria
                // o usuario procurando defeito onde nao ha.
                if (NomeDoSensor(p.Panel1Id) == null || NomeDoSensor(p.Panel2Id) == null)
                    Warn(T.ImportedUnknownSensor);
                else
                    Aviso(T.Imported(p.Name));
            }
        }

        /// <summary>Nome de perfil reduzido ao que o Windows aceita num arquivo.</summary>
        private static string NomeDeArquivo(string nome)
        {
            if (string.IsNullOrEmpty(nome)) return "perfil";
            char[] proibidos = System.IO.Path.GetInvalidFileNameChars();
            System.Text.StringBuilder sb = new System.Text.StringBuilder(nome.Length);
            foreach (char c in nome)
                sb.Append(Array.IndexOf(proibidos, c) >= 0 ? '_' : c);
            string s = sb.ToString().Trim();
            return s.Length == 0 ? "perfil" : s;
        }

        private void OnRenameProfile(object sender, EventArgs e)
        {
            Profile alvo = _profileList != null && _profileList.Selected != null ? _profileList.Selected : _current;
            string name = Prompt(T.NewName, alvo.Name);
            if (string.IsNullOrEmpty(name) || name == alvo.Name) return;
            if (_cfg.NameExists(name)) { Warn(T.NameTaken); return; }

            // Renomear o perfil ativo tem de levar o ponteiro junto: ActiveName
            // guarda o nome, e nao a referencia, entao o antigo deixaria de
            // casar com qualquer perfil e o primeiro da lista viraria o ativo.
            bool eraAtivo = string.Equals(alvo.Name, _cfg.ActiveName, StringComparison.Ordinal);
            bool eraPadrao = string.Equals(alvo.Name, _cfg.DefaultProfile.Name, StringComparison.Ordinal);
            string nomeAnterior = alvo.Name;
            alvo.Name = name;
            _cfg.RenomearPerfilNosJogos(nomeAnterior, name);
            if (eraAtivo) _cfg.ActiveName = name;
            if (eraPadrao) _cfg.DefaultProfileName = name;

            if (alvo == _current) { _nav.Subtitle = _session.ActiveProfile.Name; _nav.Invalidate(); }
            _dirty = true;
            RefreshProfileList();
        }

        private void OnDeleteProfile(object sender, EventArgs e)
        {
            if (_cfg.Profiles.Count <= 1) { Warn(T.KeepOneProfile); return; }
            Profile alvo = _profileList != null && _profileList.Selected != null ? _profileList.Selected : _current;
            SaveToProfile();
            Config antes = _cfg.Clone();
            string nomeAlvo = alvo.Name;

            _cfg.RemoverPerfilDosJogos(alvo.Name);
            _cfg.Profiles.Remove(alvo);
            if (_current == alvo) _current = _cfg.Profiles[0];
            if (string.Equals(_cfg.ActiveName, alvo.Name, StringComparison.Ordinal))
                _cfg.ActiveName = _cfg.Profiles[0].Name;
            if (string.Equals(_cfg.DefaultProfileName, alvo.Name, StringComparison.Ordinal))
                _cfg.DefaultProfileName = _cfg.NameExists(_cfg.ActiveName)
                    ? _cfg.ActiveName : _cfg.Profiles[0].Name;
            _cfg.EnsureDefaultProfile();

            _nav.Subtitle = _session.ActiveProfile.Name;
            _nav.Invalidate();
            _dirty = true;
            RefreshProfileList(); LoadFromProfile();
            OferecerDesfazer(T.ProfileDeleted(nomeAlvo), delegate { RestaurarPerfis(antes, nomeAlvo); });
        }

        /// <summary>Falha recuperavel: faixa no alto da janela, com a acao que resolve quando ha.</summary>
        private void Warn(string msg) { Warn(msg, null, null); }

        private void Warn(string msg, string textoDaAcao, Action acao)
        {
            if (_faixa == null || IsDisposed) { Log.Write("aviso: " + msg); return; }
            _faixaDeSensores = false;
            _faixa.Mostrar(msg, textoDaAcao, acao);
        }

        /// <summary>Confirmacao discreta no rodape, que se apaga sozinha.</summary>
        private void Aviso(string texto)
        {
            if (_footerNote == null) return;
            _footerNote.Text = texto;
            _footerNote.ForeColor = Ui.AccentText;
            _footerNote.Visible = _nav == null || _nav.Width >= Ui.S(120);

            if (_noteTimer == null)
            {
                _noteTimer = new Timer();
                _noteTimer.Interval = 2600;
                // Volta ao estado, e nao ao vazio: se o aviso apareceu por causa
                // de algo que deixou edicao pendente, quem some e a confirmacao,
                // nao o alerta de que ainda ha o que gravar.
                _noteTimer.Tick += delegate { _noteTimer.Stop(); AtualizarRodape(); };
            }
            _noteTimer.Stop();
            _noteTimer.Start();
        }

        /// <summary>
        /// Poe o rodape de acordo com haver ou nao edicao pendente.
        ///
        /// Um aviso no ar tem precedencia: ele dura 2,6 s e ja esta dizendo algo
        /// mais especifico ("Salvo", "Perfil aplicado"). Sobrescreve-lo aqui
        /// apagaria a confirmacao no instante em que ela apareceu, e o clique
        /// pareceria nao ter feito nada. Quando o tempo dele vence, o Tick chama
        /// este metodo e o estado volta.
        /// </summary>
        private void AtualizarRodape()
        {
            if (_footerNote == null) return;
            IReadingStatusData status = _data as IReadingStatusData;
            Control page = _nav == null || _nav.Selected == null ? null : _nav.Selected.Page;
            bool sensorPage = page == _pgVisao || page == _pgMetricas || page == _pgPaineis || page == _pgPerfis;
            AtualizarFaixaDeSensores(status, sensorPage);
            if (_noteTimer != null && _noteTimer.Enabled) return;

            // Mesmo texto do dialogo de fechar, de proposito: a barra avisa e o
            // dialogo cobra a mesma coisa, e duas redacoes para o mesmo estado
            // fariam parecer dois estados.
            string readingNote = status != null && sensorPage ? ReadingStateText(status.CurrentReadingState(null)) : "";
            // So avisa quando a gravacao falhou: com gravacao automatica, edicao
            // pendente por 350 ms nao e um estado que mereca aviso.
            bool naoGravou = _dirty && _autoPausado;
            _footerNote.Text = naoGravou ? T.UnsavedTitle : readingNote;
            _footerNote.ForeColor = Ui.Warn;
            _footerNote.Visible = (naoGravou || readingNote.Length > 0) && _nav != null && _nav.Width >= Ui.S(120);
        }

        // ---------------- sensores sem resposta ----------------

        private bool _faixaDeSensores;
        private ReadingState? _estadoAvisado;

        /// <summary>
        /// Sensores sem resposta viram faixa com "Tentar de novo", em vez de so
        /// uma nota no rodape. Dispensada no X, nao volta enquanto o estado for
        /// o mesmo; reaparece se a leitura voltar e cair de novo.
        /// </summary>
        private void AtualizarFaixaDeSensores(IReadingStatusData status, bool sensorPage)
        {
            if (_faixa == null || status == null) return;
            ReadingState estado = status.CurrentReadingState(null);
            bool caiu = estado == ReadingState.Failed || estado == ReadingState.Expired;
            if (!caiu)
            {
                _estadoAvisado = null;
                if (_faixaDeSensores) { _faixa.Ocultar(); _faixaDeSensores = false; }
                return;
            }
            if (!sensorPage)
            {
                if (_faixaDeSensores) { _faixa.Ocultar(); _faixaDeSensores = false; _estadoAvisado = null; }
                return;
            }
            if (_estadoAvisado == estado || (_faixa.Visible && !_faixaDeSensores)) return;
            _estadoAvisado = estado;
            _faixaDeSensores = true;
            _faixa.Mostrar(ReadingStateText(estado), T.TryAgain, delegate { TentarSensoresDeNovo(); });
        }

        private void TentarSensoresDeNovo()
        {
            _faixaDeSensores = false;
            _estadoAvisado = null;
            if (_data == null) return;
            try
            {
                _sensors = _data.RefreshSensorList();
                _pick1.SetSensors(Clone(_sensors));
                _pick2.SetSensors(Clone(_sensors));
                _pick1.SelectedId = _current.Panel1Id;
                _pick2.SelectedId = _current.Panel2Id;
                Refresh0();
            }
            catch (Exception ex) { Log.Error("tentar sensores de novo", ex); }
        }

        // ---------------- desfazer ----------------

        private Action _desfazer;
        private Timer _desfazerTimer;

        /// <summary>
        /// Oferece "Desfazer" por 6 s no rodape depois de um gesto destrutivo.
        /// Como tudo vale na hora, sem este recurso um clique errado em apagar
        /// seria definitivo.
        /// </summary>
        private void OferecerDesfazer(string texto, Action desfazer)
        {
            _desfazer = desfazer;
            Aviso(texto);
            if (_btDesfazer != null) { _btDesfazer.Visible = true; _btDesfazer.BringToFront(); }
            if (_desfazerTimer == null)
            {
                _desfazerTimer = new Timer();
                _desfazerTimer.Interval = 6000;
                _desfazerTimer.Tick += delegate { EncerrarDesfazer(); };
            }
            _desfazerTimer.Stop();
            _desfazerTimer.Start();
        }

        private void EncerrarDesfazer()
        {
            if (_desfazerTimer != null) _desfazerTimer.Stop();
            _desfazer = null;
            if (_btDesfazer != null) _btDesfazer.Visible = false;
        }

        private void Desfazer()
        {
            Action a = _desfazer;
            EncerrarDesfazer();
            if (a == null) return;
            a();
            Aviso(T.Undone);
        }

        /// <summary>Devolve os perfis e vinculos de jogo de uma copia anterior, sem tocar no resto.</summary>
        private void RestaurarPerfis(Config antes, string selecionado)
        {
            _cfg.Profiles.Clear();
            foreach (Profile p in antes.Profiles) _cfg.Profiles.Add(p.Clone());
            _cfg.ActiveName = antes.ActiveName;
            _cfg.DefaultProfileName = antes.DefaultProfileName;
            _cfg.GameKeys.Clear(); _cfg.GameKeys.AddRange(antes.GameKeys);
            _cfg.GameProfileNames.Clear(); _cfg.GameProfileNames.AddRange(antes.GameProfileNames);
            _cfg.GameDisplayNames.Clear(); _cfg.GameDisplayNames.AddRange(antes.GameDisplayNames);
            _cfg.GamePaths.Clear(); _cfg.GamePaths.AddRange(antes.GamePaths);
            Profile alvo = null;
            foreach (Profile p in _cfg.Profiles) if (p.Name == selecionado) alvo = p;
            _current = alvo ?? _cfg.Profiles[0];
            if (antes.ActiveName == selecionado) _selectionAtLastSave = null;   // o proximo gravar reativa
            _nav.Subtitle = _session.ActiveProfile.Name;
            _nav.Invalidate();
            _dirty = true;
            RefreshProfileList(); LoadFromProfile();
            AtualizarJogo();
        }

        private void RestaurarMetricas(Config antes)
        {
            _cfg.MetricIds.Clear(); _cfg.MetricIds.AddRange(antes.MetricIds);
            _cfg.MetricSizes.Clear(); _cfg.MetricSizes.AddRange(antes.MetricSizes);
            _cfg.MetricRange = antes.MetricRange;
            _cfg.MetricsChosen = antes.MetricsChosen;
            _cfg.MetricProfiles.Clear();
            foreach (MetricProfile p in antes.MetricProfiles) _cfg.MetricProfiles.Add(p.Clone());
            if (!GravarMetricas()) return;
            MontarMetricas();
            if (Applied != null) Applied(this, EventArgs.Empty);
        }
    }
}
