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

        public TextPromptForm(
            Color accent,
            float scale,
            string title,
            string hint,
            string okText = "OK",
            string cancelText = "Cancel",
            params PromptField[] fields)
        {
            // Sans champ, la boite sert de simple confirmation.
            _fields = fields;

            int Scaled(int amount) => (int)Math.Round(amount * scale);

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            BackColor = Color.FromArgb(18, 18, 22);
            AutoScaleMode = AutoScaleMode.None;
            KeyPreview = true;

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
                MaximumSize = new Size(Scaled(412), 0),
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
                    Font = new Font("Segoe UI", 10f),
                    ForeColor = accent,
                    MaxLength = 260,
                    Multiline = true,
                    Location = new Point(Scaled(24), y),
                    Size = new Size(Scaled(hasBrowse ? 320 : 412), Scaled(28)),
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
                Location = new Point(Scaled(352), y + Scaled(6)),
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
            ClientSize = new Size(Scaled(460), y + Scaled(58));

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

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            // Filet d'accent autour de la boite, puisqu'elle n'a pas de bordure systeme.
            using var pen = new Pen(Color.FromArgb(120, 232, 185, 35));
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
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
