using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace MhiagosControl
{
    public partial class SettingsForm
    {
        private Card _backupCard, _diagnosticCard;
        private FlatBtn _gameFileButton;
        private Label _gameFileNote, _gameBindingsHeading;

        private Label _atalhosDaConfig;

        /// <summary>Backup e diagnostico, as duas ultimas linhas do grupo Dados, e a nota dos atalhos.</summary>
        private void AddDataTools(Control page)
        {
            // E78C: salvar; E90F: chave inglesa, o "reparar" da Segoe MDL2
            LinhaDeOpcao backup = LinhaDaConfig(page, new LinhaDeOpcao("\uE78C", T.BackupTitle, T.BackupShort));
            _dicaDaConfig.SetToolTip(backup.Descricao, T.BackupNote);
            backup.Direita(BotaoDaLinha(T.BackupCreate, delegate { ExportBackup(); }));
            backup.Direita(BotaoDaLinha(T.BackupRestore, delegate { ImportBackup(); }));
            _backupCard = backup;

            LinhaDeOpcao diagnostico = LinhaDaConfig(page, new LinhaDeOpcao("\uE90F", T.DiagnosticTitle, T.DiagnosticShort));
            _dicaDaConfig.SetToolTip(diagnostico.Descricao, T.DiagnosticNote);
            diagnostico.Direita(BotaoDaLinha(T.DiagnosticOpen, delegate { ShowDiagnostic(); }));
            _diagnosticCard = diagnostico;

            _atalhosDaConfig = MakeLabel(T.KeyboardHelp, 0, 0, Ui.FontSmall);
            _atalhosDaConfig.ForeColor = Ui.Muted;
            page.Controls.Add(_atalhosDaConfig);
            _itensDaConfig.Add(_atalhosDaConfig);
        }

        private void ExportBackup()
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = T.BackupCreate;
                dialog.Filter = "Mhiagos backup (*.mhiagos-backup)|*.mhiagos-backup";
                dialog.FileName = "MhiagosControl-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".mhiagos-backup";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try { ConfigurationBackup.Write(_session.SavedConfiguration, dialog.FileName); Aviso(T.FileSaved); }
                catch (Exception ex) { Warn(T.FileFailed + ex.Message); }
            }
        }

        private void ImportBackup()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = T.BackupRestore;
                dialog.Filter = "Mhiagos backup (*.mhiagos-backup)|*.mhiagos-backup";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    Config candidate = ConfigurationBackup.Read(dialog.FileName);
                    string summary = T.RestoreNote + "\r\n\r\n" +
                        T.SavedProfiles + ": " + candidate.Profiles.Count + "\r\n" +
                        T.GameBindings + ": " + candidate.GameKeys.Count + "\r\n" +
                        T.NavMetrics + ": " + candidate.MetricIds.Count + "\r\n" +
                        (T.Pt ? "Grades salvas: " : "Saved grids: ") + candidate.MetricProfiles.Count + "\r\n" +
                        T.ActiveProfile + ": " + candidate.ActiveName + "\r\n" +
                        (T.Pt ? "Sensores não encontrados: " : "Sensors not found: ") +
                        ConfigurationBackup.MissingSensors(candidate, _sensors) + "\r\n\r\n" +
                        (T.Pt ? "Sensores ausentes não serão substituídos automaticamente." : "Missing sensors will not be replaced automatically.");
                    using (TextReviewDialog review = new TextReviewDialog(T.RestorePreview, summary, T.RestoreApply))
                        if (review.ShowDialog(this) != DialogResult.OK) return;
                    string safetyPath, error;
                    if (!_session.TryRestore(candidate, out safetyPath, out error)) { Warn(T.FileFailed + error); return; }
                    _dirty = false; _saved = true;
                    if (_data != null) _data.SetShowAllSensors(_cfg.ShowAllSensors);
                    T.Language = string.IsNullOrEmpty(_cfg.Language) ? T.Detect() : _cfg.Language;
                    if (Applied != null) Applied(this, EventArgs.Empty);
                    DialogResult = DialogResult.Retry;
                    Close();
                }
                catch (Exception ex) { Warn(T.FileFailed + ex.Message); }
            }
        }

        private void ShowDiagnostic()
        {
            try
            {
                IPanelStatusData source = _data as IPanelStatusData;
                string text = SupportReport.Build(_session.SavedConfiguration, _sensors,
                    _data == null ? null : _data.CurrentSnapshot(),
                    source == null ? null : source.CurrentPanelStatus(), Rtss.Presente(), DateTime.UtcNow);
                using (TextReviewDialog review = new TextReviewDialog(T.DiagnosticTitle, text, T.SaveFile))
                    if (review.ShowDialog(this) != DialogResult.OK) return;
                using (SaveFileDialog dialog = new SaveFileDialog())
                {
                    dialog.Filter = "Text (*.txt)|*.txt";
                    dialog.FileName = "MhiagosControl-diagnostic-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt";
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    File.WriteAllText(dialog.FileName, text, Encoding.UTF8);
                    Aviso(T.FileSaved);
                }
            }
            catch (Exception ex) { Warn(T.FileFailed + ex.Message); }
        }

        private void BrowseGame()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = T.GameBrowse;
                dialog.Filter = "Executable (*.exe)|*.exe";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    string key = GameFileBinding.Key(dialog.FileName);
                    if (_cfg.PerfilDoJogo(key) != null && MessageBox.Show(this, T.GameReplace,
                        T.GameBrowse, MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                        MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                    GameFileBinding.Bind(_cfg, dialog.FileName, _current.Name);
                    _gameIdentityCache.Remove(key); _gameBindingsSignature = null;
                    _dirty = true;
                    AtualizarJogo();
                    Aviso(T.GameBound(GameIdentity.Humanize(key), _current.Name));
                }
                catch (Exception ex) { Warn(T.FileFailed + ex.Message); }
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.S)) { if (_dirty) OnSave(this, EventArgs.Empty); return true; }
            if (keyData == (Keys.Control | Keys.B)) { _nav.Collapsed = !_nav.Collapsed; return true; }
            if ((keyData & Keys.Modifiers) == Keys.Alt)
            {
                int index = (int)(keyData & Keys.KeyCode) - (int)Keys.D1;
                // Alt+1 a Alt+9 seguem a ordem que esta na barra, mesmo reorganizada
                if (index >= 0 && index < Math.Min(9, _nav.Items.Count)) { _nav.SelectedIndex = index; return true; }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>
        /// Titulos de grupo, linhas e a nota dos atalhos, de cima para baixo na
        /// largura que a pagina tem. Cada linha decide sozinha se os controles
        /// cabem ao lado do texto ou descem para baixo dele.
        /// </summary>
        private void ArrangeSettings()
        {
            if (_pgConfig == null || !_pgConfig.Visible) return;
            int width = Math.Max(300, Math.Min(LarguraMaxDaPagina, _pgConfig.ClientSize.Width - Pagina.LarguraDaBarra - 4));
            int y = 0;
            bool primeiro = true;
            foreach (Control item in _itensDaConfig)
            {
                LinhaDeOpcao linha = item as LinhaDeOpcao;
                if (linha != null)
                {
                    linha.Location = new Point(0, y);
                    linha.Arranjar(width);
                    y = linha.Bottom + 4;
                }
                else if ((item.Tag as string) == "grupo")
                {
                    if (!primeiro) y += 18;
                    item.SetBounds(2, y, width - 4, 24);
                    y = item.Bottom + 6;
                }
                else
                {
                    int h = item is Label ? ((Label)item).GetPreferredSize(new Size(width - 4, 0)).Height : item.Height;
                    item.SetBounds(2, y + 10, width - 4, Math.Max(18, h));
                    y = item.Bottom + 8;
                }
                primeiro = false;
            }
            ((Pagina)_pgConfig).Sincronizar();
        }

        /// <summary>
        /// Duas colunas: a lista com o Rodizio embaixo, e a previa com o Perfil
        /// por jogo embaixo, as duas terminando juntas. Em janela estreita tudo
        /// empilha: lista, previa, jogo e rodizio.
        /// </summary>
        private void ArrangeProfiles()
        {
            if (_pgPerfis == null || !_pgPerfis.Visible || _profilesPreviewCard == null) return;
            const int Esp = 12;
            int width = Math.Max(320, Math.Min(LarguraMaxDaPagina, _pgPerfis.Width - Pagina.LarguraDaBarra - 4));
            bool lado = width >= 700;
            int lw = lado ? (width - Esp) * 40 / 100 : width;
            int rw = lado ? width - lw - Esp : width;

            // a largura antes dos filhos: os ancorados a direita andam com ela
            _profilesListCard.Width = lw;
            _profilesPreviewCard.Width = rw;
            _cardGame.Width = rw;
            _cardRotation.Width = lw;

            // ---- previa: imagem a esquerda e os quatro botoes ao lado; se a
            // coluna for estreita, os botoes descem para baixo da imagem
            bool botoesAoLado = rw >= 520;
            int colBotoes = botoesAoLado ? Math.Max(170, Math.Min(240, (rw - 24) * 42 / 100)) : 0;
            int pw = botoesAoLado ? rw - 24 - colBotoes - 12 : rw - 24;
            int ph = Math.Max(150, Math.Min(260, pw * 250 / 386));
            _profilePreview.SetBounds(12, 44, pw, ph);
            FlatBtn[] daPrevia = { _profileSensor1, _profileSensor2, _btnDefault, _btnApply };
            int alturaPrevia;
            if (botoesAoLado)
            {
                int bx = _profilePreview.Right + 12;
                int total = 4 * 34 + 3 * 8;
                int by = 44 + Math.Max(0, (ph - total) / 2);
                foreach (FlatBtn b in daPrevia) { b.SetBounds(bx, by, colBotoes, 34); by += 42; }
                alturaPrevia = Math.Max(_profilePreview.Bottom, by - 8) + 14;
            }
            else
            {
                int bw = (pw - 8) / 2, by = _profilePreview.Bottom + 12;
                for (int i = 0; i < 4; i++)
                    daPrevia[i].SetBounds(12 + (i % 2) * (bw + 8), by + (i / 2) * 42, i % 2 == 0 ? bw : pw - bw - 8, 34);
                alturaPrevia = by + 42 + 34 + 14;
            }

            // ---- perfil por jogo, na largura da coluna da direita
            bool compactGame = rw < 620;
            _tgJogo.Width = rw - 32;
            _lbJogoNome.AutoEllipsis = true;
            _lbJogoNome.SetBounds(82, 106, rw - 98 - (compactGame ? 0 : 250), 26);
            _lbJogoAtual.Width = _lbJogoNome.Width;
            if (compactGame)
            {
                int buttonWidth = (rw - 40) / 2;
                _btVincular.SetBounds(16, 174, buttonWidth, 34);
                _btDesvincular.SetBounds(24 + buttonWidth, 174, buttonWidth, 34);
            }
            else
            {
                _btVincular.SetBounds(rw - 246, 106, 230, 34);
                _btDesvincular.SetBounds(rw - 246, 146, 230, 30);
            }
            _lbJogoNota.SetBounds(16, compactGame ? 218 : 177, rw - 32, 38);
            _gameFileButton.Top = compactGame ? 262 : 220;
            _gameFileNote.SetBounds(16, compactGame ? 302 : 260, rw - 32, 50);
            _gameBindingsHeading.Top = compactGame ? 361 : 319;
            _gameBindings.SetBounds(16, compactGame ? 388 : 346, rw - 32, _gameBindings.Height);
            int alturaJogo = _gameBindings.Bottom + 16;

            // ---- rodizio, na largura da coluna da esquerda: estreito, o rotulo
            // do interruptor quebra em duas linhas e a nota desce
            bool rotuloCabe = 48 + TextRenderer.MeasureText(T.IncludeInRotation, Ui.FontBase).Width <= lw - 32;
            _tgRotate.SetBounds(16, 48, lw - 32, rotuloCabe ? 26 : 42);
            _rotSecs.Top = _tgRotate.Bottom + 10;
            bool notaAoLado = lw >= 480;
            _rotNote.AutoSize = false;
            if (notaAoLado) _rotNote.SetBounds(134, _rotSecs.Top, lw - 150, 32);
            else _rotNote.SetBounds(16, _rotSecs.Bottom + 8, lw - 32, Math.Max(32, _rotNote.GetPreferredSize(new Size(lw - 32, 0)).Height));
            int alturaRodizio = Math.Max(150, _rotNote.Bottom + 16);

            // ---- lista: grade de tres por dois embaixo, ou dois por tres se estreita
            int colunas = lw >= 330 ? 3 : 2;
            int linhasDeBotoes = (_botoesDePerfil.Count + colunas - 1) / colunas;
            int alturaDosBotoes = linhasDeBotoes * 32 + (linhasDeBotoes - 1) * 8;
            int y;
            if (lado)
            {
                int direita = alturaPrevia + Esp + alturaJogo;
                int alturaLista = Math.Max(44 + 150 + 12 + alturaDosBotoes + 14, direita - Esp - alturaRodizio);
                _profilesListCard.SetBounds(0, 0, lw, alturaLista);
                _cardRotation.SetBounds(0, _profilesListCard.Bottom + Esp, lw, alturaRodizio);
                _profilesPreviewCard.SetBounds(lw + Esp, 0, rw, alturaPrevia);
                // a coluna da direita estica o jogo para as duas terminarem juntas
                _cardGame.SetBounds(lw + Esp, _profilesPreviewCard.Bottom + Esp, rw,
                    Math.Max(alturaJogo, _cardRotation.Bottom - _profilesPreviewCard.Bottom - Esp));
                y = Math.Max(_cardRotation.Bottom, _cardGame.Bottom) + Esp;
            }
            else
            {
                int alturaLista = 44 + Math.Min(320, Math.Max(150, _cfg.Profiles.Count * 54 + 8)) + 12 + alturaDosBotoes + 14;
                _profilesListCard.SetBounds(0, 0, width, alturaLista);
                _profilesPreviewCard.SetBounds(0, _profilesListCard.Bottom + Esp, width, alturaPrevia);
                _cardGame.SetBounds(0, _profilesPreviewCard.Bottom + Esp, width, alturaJogo);
                _cardRotation.SetBounds(0, _cardGame.Bottom + Esp, width, alturaRodizio);
                y = _cardRotation.Bottom + Esp;
            }

            int larguraLista = _profilesListCard.Width - 24;
            int bwl = (larguraLista - (colunas - 1) * 8) / colunas;
            int topoBotoes = _profilesListCard.Height - 14 - alturaDosBotoes;
            for (int i = 0; i < _botoesDePerfil.Count; i++)
            {
                int col = i % colunas, lin = i / colunas;
                int w = col == colunas - 1 ? larguraLista - (colunas - 1) * (bwl + 8) : bwl;
                _botoesDePerfil[i].SetBounds(12 + col * (bwl + 8), topoBotoes + lin * 40, w, 32);
            }
            _profileList.SetBounds(12, 44, larguraLista, topoBotoes - 12 - 44);

            _profilesNote.SetBounds(2, y, width - 4, 48);
            ((Pagina)_pgPerfis).Sincronizar();
        }

        internal static Size FitInitialClient(Size requested, Size workArea)
        {
            return new Size(Math.Min(requested.Width, Math.Max(320, workArea.Width - 40)),
                Math.Min(requested.Height, Math.Max(320, workArea.Height - 80)));
        }

        private void ArrangeAboutBounds(Control page)
        {
            if (page == null || !page.Visible) return;
            int width = Math.Min(LarguraMaxDaPagina, page.Width - Pagina.LarguraDaBarra - 4);
            int margin = Math.Max(0, (page.Width - Pagina.LarguraDaBarra - 4 - width) / 2);
            int y = 0;
            foreach (Control card in page.Controls)
            {
                if (!(card is Card)) continue;
                Rectangle cardOriginal = Projeto(card);
                foreach (Control child in card.Controls) Projeto(child);
                card.SetBounds(margin, y, width, cardOriginal.Height);
                int bottom = 0;
                foreach (Control child in card.Controls)
                {
                    Rectangle original = Projeto(child);
                    child.Bounds = original;
                    Label label = child as Label;
                    if (label != null)
                    {
                        child.Width = Math.Max(40, Math.Min(original.Width, width - child.Left - 16));
                        child.Height = Math.Max(original.Height,
                            label.GetPreferredSize(new Size(child.Width, 0)).Height + 4);
                    }
                    // Uma linha que cresceu empurra somente o conteudo abaixo
                    // dela; colunas que ja coexistiam na mesma faixa permanecem.
                    int shift = 0;
                    foreach (Control previous in card.Controls)
                    {
                        if (previous == child) break;
                        Rectangle before = Projeto(previous);
                        if (original.Top >= before.Bottom) shift = Math.Max(shift, previous.Bottom - before.Bottom);
                    }
                    child.Top = original.Top + shift;
                    bottom = Math.Max(bottom, child.Bottom);
                }
                card.Height = Math.Max(cardOriginal.Height, bottom + 16);
                y = card.Bottom + 12;
            }
            ((Pagina)page).Sincronizar();
        }
    }

    internal static class GameFileBinding
    {
        internal static string Key(string path)
        {
            if (!File.Exists(path) || !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(T.InvalidGameFile);
            string key = Path.GetFileName(path);
            if (key.IndexOfAny(new char[] { '|', '\r', '\n' }) >= 0) throw new ArgumentException(T.InvalidGameFile);
            return key;
        }

        internal static void Bind(Config draft, string path, string profile)
        {
            string key = Key(path);
            if (!draft.NameExists(profile)) throw new ArgumentException(T.InvalidGameFile);
            draft.MapearJogo(key, profile);
            draft.IdentificarJogo(key, GameIdentity.Humanize(key), Path.GetFullPath(path));
        }
    }

    internal sealed class TextReviewDialog : Form
    {
        internal TextReviewDialog(string title, string text, string action)
        {
            Text = title; Icon = Assets.AppIcon; BackColor = Ui.Window; ForeColor = Ui.Text;
            Font = Ui.FontBase; StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false; ShowInTaskbar = false;
            MinimumSize = new Size(480, 360);
            ClientSize = SettingsForm.FitInitialClient(new Size(680, 510), Screen.FromPoint(Cursor.Position).WorkingArea.Size);
            TextBox content = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                Text = text, BackColor = Ui.SurfaceAlt, ForeColor = Ui.Text, BorderStyle = BorderStyle.FixedSingle };
            content.SetBounds(16, 16, ClientSize.Width - 32, ClientSize.Height - 84);
            content.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(content);
            FlatBtn cancel = new FlatBtn { Text = T.Cancel };
            cancel.SetBounds(ClientSize.Width - 330, ClientSize.Height - 52, 130, 36);
            cancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);
            FlatBtn apply = new FlatBtn { Text = action, Primary = true };
            apply.SetBounds(ClientSize.Width - 190, ClientSize.Height - 52, 174, 36);
            apply.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            apply.Click += delegate { DialogResult = DialogResult.OK; Close(); };
            Controls.Add(apply);
            cancel.TabIndex = 0; apply.TabIndex = 1; content.TabIndex = 2;
            ActiveControl = cancel;
            Shown += delegate { content.Select(0, 0); cancel.Focus(); };
            Theme.Apply(this);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
