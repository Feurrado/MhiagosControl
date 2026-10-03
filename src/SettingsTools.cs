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

        private void AddDataTools(Control page)
        {
            _backupCard = new Card { Title = T.BackupTitle };
            _backupCard.SetBounds(0, 598, 756, 188);
            Label backupNote = MakeLabel(T.BackupNote, 16, 48, Ui.FontBase);
            backupNote.SetBounds(16, 48, 724, 70);
            backupNote.ForeColor = Ui.Muted;
            _backupCard.Controls.Add(backupNote);
            _backupCard.Controls.Add(MakeSideButton(T.BackupCreate, 16, 132, 200, delegate { ExportBackup(); }));
            _backupCard.Controls.Add(MakeSideButton(T.BackupRestore, 228, 132, 200, delegate { ImportBackup(); }));
            page.Controls.Add(_backupCard);

            _diagnosticCard = new Card { Title = T.DiagnosticTitle };
            _diagnosticCard.SetBounds(0, 798, 756, 202);
            Label note = MakeLabel(T.DiagnosticNote, 16, 48, Ui.FontBase);
            note.SetBounds(16, 48, 724, 64); note.ForeColor = Ui.Muted;
            _diagnosticCard.Controls.Add(note);
            _diagnosticCard.Controls.Add(MakeSideButton(T.DiagnosticOpen, 16, 124, 220,
                delegate { ShowDiagnostic(); }));
            Label keys = MakeLabel(T.KeyboardHelp, 16, 168, Ui.FontSmall);
            keys.Size = new Size(724, 28); keys.ForeColor = Ui.Muted;
            _diagnosticCard.Controls.Add(keys);
            page.Controls.Add(_diagnosticCard);
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
                if (index >= 0 && index < 7) { _nav.SelectedIndex = index; return true; }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void ArrangeSettings()
        {
            if (_pgConfig == null || !_pgConfig.Visible) return;
            int width = Math.Min(LarguraMaxDaPagina, _pgConfig.ClientSize.Width - Pagina.LarguraDaBarra - 4);
            int y = 0;
            foreach (Control card in _pgConfig.Controls)
            {
                if (!(card is Card)) continue;
                card.SetBounds(0, y, width, card.Height);
                foreach (Control child in card.Controls)
                {
                    Label label = child as Label;
                    if (label != null && label.Width > 250)
                        label.Width = Math.Max(50, width - label.Left - 16);
                    if (child.Name == "dataFolder")
                    {
                        child.Width = Math.Max(50, width - 188);
                        ((Label)child).AutoEllipsis = true;
                    }
                    if (child is Toggle) child.Width = width - child.Left - 16;
                }
                if (card.Controls.Contains(_btRtss))
                {
                    int index = 0;
                    int columns = width < 580 ? 2 : 3;
                    int buttonWidth = (width - 32 - (columns - 1) * 8) / columns;
                    foreach (Control child in card.Controls)
                        if (child is FlatBtn)
                        {
                            child.SetBounds(16 + (index % columns) * (buttonWidth + 8),
                                128 + (index / columns) * 40, buttonWidth, 32);
                            index++;
                        }
                    card.Height = columns == 2 ? 212 : 172;
                }
                y += card.Height + 12;
            }
            ((Pagina)_pgConfig).Sincronizar();
        }

        private void ArrangeProfiles()
        {
            if (_pgPerfis == null || !_pgPerfis.Visible || _profilesPreviewCard == null) return;
            int width = Math.Min(LarguraMaxDaPagina, _pgPerfis.Width - Pagina.LarguraDaBarra - 4);
            bool narrow = width < 756;
            _profileList.Height = narrow ? Math.Min(300, Math.Max(150, _cfg.Profiles.Count * 54 + 8)) : 300;
            foreach (Control child in _profilesListCard.Controls)
                if (child is FlatBtn)
                {
                    Rectangle original = Projeto(child);
                    child.Top = _profileList.Bottom + 12 + (original.Top - 356);
                }
            int listHeight = _profileList.Bottom + 156;
            _profilesListCard.SetBounds(0, 0, narrow ? width : (width - 16) * 44 / 100, listHeight);
            _profilesPreviewCard.SetBounds(width < 756 ? 0 : _profilesListCard.Right + 16,
                narrow ? _profilesListCard.Bottom + 12 : 0, narrow ? width : width - _profilesListCard.Width - 16, 500);
            _cardGame.SetBounds(0, _profilesPreviewCard.Bottom + 12, width, _cardGame.Height);
            bool compactGame = width < 620;
            _tgJogo.Width = width - 32;
            _lbJogoNome.AutoEllipsis = true;
            _lbJogoNome.SetBounds(82, 106, width - 98 - (compactGame ? 0 : 250), 26);
            _lbJogoAtual.Width = _lbJogoNome.Width;
            if (compactGame)
            {
                int buttonWidth = (width - 40) / 2;
                _btVincular.SetBounds(16, 174, buttonWidth, 34);
                _btDesvincular.SetBounds(24 + buttonWidth, 174, buttonWidth, 34);
            }
            else
            {
                _btVincular.SetBounds(width - 246, 106, 230, 34);
                _btDesvincular.SetBounds(width - 246, 146, 230, 30);
            }
            _lbJogoNota.SetBounds(16, compactGame ? 218 : 177, width - 32, 38);
            _gameFileButton.Top = compactGame ? 262 : 220;
            _gameFileNote.SetBounds(16, compactGame ? 302 : 260, width - 32, 50);
            _gameBindingsHeading.Top = compactGame ? 361 : 319;
            _gameBindings.Top = compactGame ? 388 : 346;
            _cardGame.Height = _gameBindings.Bottom + 16;
            _cardRotation.SetBounds(0, _cardGame.Bottom + 12, width, 150);
            _rotNote.Width = Math.Max(50, width - _rotNote.Left - 16);
            _tgRotate.Width = width - 32;
            _profilesNote.SetBounds(2, _cardRotation.Bottom + 12, width - 4, 48);
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
