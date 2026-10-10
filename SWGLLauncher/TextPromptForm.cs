using System.Runtime.InteropServices;

namespace SWGLLauncher
{
    /// <summary>Type de selection propose a cote d'un champ de saisie.</summary>
    internal enum PromptBrowse
    {
        None,
        Folder,
        File,
    }

    /// <summary>Un champ de la boite de dialogue.</summary>
    /// <param name="Label">Libelle affiche au-dessus du champ, vide pour aucun.</param>
    /// <param name="Value">Valeur initiale.</param>
    /// <param name="Browse">Bouton de selection associe, s'il y en a un.</param>
    /// <param name="Required">Vrai si la validation exige une valeur.</param>
    internal sealed record PromptField(
        string Label = "",
        string Value = "",
        PromptBrowse Browse = PromptBrowse.None,
        bool Required = true);

    /// <summary>
    /// Boite de dialogue sans bordure, au style du launcher : un titre, une aide et
    /// un ou plusieurs champs, avec au besoin un bouton de selection de dossier ou de fichier.
    /// </summary>
    internal sealed class TextPromptForm : Form
    {
        private const int WM_NCHITTEST = 0x0084;
        private const int HTCLIENT = 1;
        private const int HTCAPTION = 2;

        private readonly List<TextBox> _inputs = [];
        private readonly PromptField[] _fields;
        private readonly int _cornerRadius;

        public TextPromptForm(
            Color accent,
            float scale,
            string title,
            string hint,
            string okText = "OK",
            string cancelText = "Cancel",
            params PromptField[] fields)
        {
            // Sans champ, la boite sert de simple confirmation ; sans texte d'annulation, de
            // simple avis a un seul bouton.
            _fields = fields;

            int Scaled(int amount) => (int)Math.Round(amount * scale);

            _cornerRadius = Scaled(RoundedWindow.DesignRadius);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            BackColor = Color.FromArgb(18, 18, 22);
            AutoScaleMode = AutoScaleMode.None;
            KeyPreview = true;

            // Largeur du contenu : 412 au minimum, elargie pour qu'un chemin pre-rempli
            // tienne en entier dans son champ (jusqu'a 720).
            var inputFont = new Font("Segoe UI", 10f);
            int browseWidth = Scaled(92);
            int contentWidth = Scaled(412);

            foreach (PromptField field in _fields)
            {
                int needed = TextRenderer.MeasureText(field.Value, inputFont).Width + Scaled(16)
                    + (field.Browse != PromptBrowse.None ? browseWidth : 0);
                contentWidth = Math.Max(contentWidth, Math.Min(needed, Scaled(720)));
            }

            var titleLabel = new Label
            {
                Text = title,
                Font = new Font("Segoe UI Semibold", 11f),
                ForeColor = accent,
                AutoSize = true,
                Location = new Point(Scaled(24), Scaled(22)),
            };

            var hintLabel = new Label
            {
                Text = hint,
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(190, 200, 200, 205),
                AutoSize = true,
                MaximumSize = new Size(contentWidth, 0),
                Location = new Point(Scaled(24), Scaled(50)),
            };

            Controls.AddRange([titleLabel, hintLabel]);

            int y = Scaled(50) + hintLabel.PreferredHeight + Scaled(18);

            foreach (PromptField field in _fields)
            {
                if (field.Label.Length > 0)
                {
                    var fieldLabel = new Label
                    {
                        Text = field.Label,
                        Font = new Font("Segoe UI", 8.5f),
                        ForeColor = Color.FromArgb(170, 200, 200, 205),
                        AutoSize = true,
                        Location = new Point(Scaled(24), y),
                    };

                    Controls.Add(fieldLabel);
                    y += Scaled(18);
                }

                bool hasBrowse = field.Browse != PromptBrowse.None;

                var input = new TextBox
                {
                    BackColor = Color.FromArgb(28, 28, 32),
                    BorderStyle = BorderStyle.FixedSingle,
                    Font = inputFont,
                    ForeColor = accent,
                    MaxLength = 260,
                    Multiline = true,
                    WordWrap = false,
                    Location = new Point(Scaled(24), y),
                    Size = new Size(hasBrowse ? contentWidth - browseWidth : contentWidth, Scaled(28)),
                    Text = field.Value,
                };

                Controls.Add(input);
                _inputs.Add(input);

                if (hasBrowse)
                {
                    PromptField captured = field;
                    TextBox target = input;

                    var browseButton = new FlatButton
                    {
                        Text = "Browse...",
                        AccentColor = accent,
                        Location = new Point(input.Right + Scaled(8), input.Top),
                        Size = new Size(Scaled(84), Scaled(28)),
                    };

                    browseButton.Click += (_, _) => Browse(target, captured.Browse);
                    Controls.Add(browseButton);
                }

                y += Scaled(28) + Scaled(14);
            }

            var cancel = new FlatButton
            {
                Text = cancelText,
                AccentColor = accent,
                Size = new Size(Scaled(84), Scaled(28)),
                Location = new Point(Scaled(24) + contentWidth - Scaled(84), y + Scaled(6)),
            };

            int okWidth = Scaled(Math.Max(84, (okText.Length * 8) + 24));

            var ok = new FlatButton
            {
                Text = okText,
                AccentColor = accent,
                Primary = true,
                Size = new Size(okWidth, Scaled(28)),
                Location = new Point(cancel.Left - Scaled(8) - okWidth, y + Scaled(6)),
            };

            // Sans texte d'annulation, simple avis : le seul bouton prend la place d'Annuler.
            if (cancelText.Length == 0)
            {
                cancel.Visible = false;
                ok.Left = cancel.Right - okWidth;
            }

            ok.Click += (_, _) => Accept();
            cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

            KeyDown += (_, e) =>
            {
                if (e.KeyCode is Keys.Enter or Keys.Return)
                {
                    e.SuppressKeyPress = true;
                    Accept();
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                }
            };

            Controls.AddRange([ok, cancel]);
            ClientSize = new Size(contentWidth + Scaled(48), y + Scaled(58));

            if (_inputs.Count > 0)
            {
                ActiveControl = _inputs[0];
            }
        }

        /// <summary>Valeur du premier champ, sans espaces superflus.</summary>
        public string Value => _inputs.Count > 0 ? ValueAt(0) : string.Empty;

        /// <summary>Valeur du champ indique, sans espaces superflus.</summary>
        public string ValueAt(int index) => _inputs[index].Text.Trim();

        private static void Browse(TextBox target, PromptBrowse mode)
        {
            string current = target.Text.Trim();

            if (mode == PromptBrowse.Folder)
            {
                using var folderDialog = new FolderBrowserDialog
                {
                    UseDescriptionForTitle = true,
                    Description = "Select a folder",
                    SelectedPath = Directory.Exists(current) ? current : string.Empty,
                };

                if (folderDialog.ShowDialog(target.FindForm()) == DialogResult.OK)
                {
                    target.Text = folderDialog.SelectedPath;
                }

                return;
            }

            using var fileDialog = new OpenFileDialog
            {
                Filter = "Applications (*.exe)|*.exe|All files (*.*)|*.*",
                CheckFileExists = true,
                FileName = File.Exists(current) ? current : string.Empty,
            };

            if (fileDialog.ShowDialog(target.FindForm()) == DialogResult.OK)
            {
                target.Text = fileDialog.FileName;
            }
        }

        private void Accept()
        {
            for (int i = 0; i < _fields.Length; i++)
            {
                if (_fields[i].Required && ValueAt(i).Length == 0)
                {
                    ActiveControl = _inputs[i];
                    return;
                }
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            RoundedWindow.Apply(this, _cornerRadius);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            // Filet d'accent le long du bord arrondi, puisque la boite n'a pas de bordure systeme.
            RoundedWindow.DrawBorder(e.Graphics, this, Color.FromArgb(120, 232, 185, 35), _cornerRadius);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            if (m.Msg == WM_NCHITTEST && m.Result == HTCLIENT)
            {
                m.Result = HTCAPTION;
            }
        }
    }
}
