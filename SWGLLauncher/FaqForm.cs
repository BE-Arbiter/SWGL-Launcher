using System.Runtime.InteropServices;

namespace SWGLLauncher
{
    /// <summary>
    /// Petite FAQ des erreurs les plus courantes, en accordeon : chaque message tel
    /// qu'affiche par le launcher, en couleur d'accent ; un clic deplie dessous sa cause
    /// et les solutions a essayer.
    /// </summary>
    internal sealed class FaqForm : Form
    {
        private const int WM_NCHITTEST = 0x0084;
        private const int HTCLIENT = 1;
        private const int HTCAPTION = 2;

        private const string Collapsed = "▸";
        private const string Expanded = "▾";

        private static readonly Color PageBackColor = Color.FromArgb(18, 18, 22);
        private static readonly Color HoverBackColor = Color.FromArgb(30, 30, 36);
        private static readonly Color DividerColor = Color.FromArgb(44, 44, 52);
        private static readonly Color AnswerColor = Color.FromArgb(200, 205, 210);
        private static readonly Color SectionColor = Color.FromArgb(130, 136, 142);

        /// <summary>Une erreur : le message affiche, sa cause, puis les solutions a essayer dans l'ordre.</summary>
        private sealed record Entry(string Error, string Cause, string[] Solutions);

        private static readonly (string Section, Entry[] Entries)[] Sections =
        [
            ("Launcher",
            [
                new("Game not found",
                    "The game executable (SWGL_SP.x86_64.exe) is missing from the install folder.",
                [
                    "Click Update to download the missing files.",
                    "Make sure the launcher is in the SWGL game folder, next to the game executable.",
                    "Check that your antivirus has not quarantined the executable, and restore it if so.",
                ]),
                new("Wrong folder selected",
                    "The folder chosen in Options > Configure Jedi Outcast is not a Jedi Outcast install "
                    + "(jk2sp.exe, jk2mp.exe and jk2gamex86.dll were not found).",
                [
                    "Select the Jedi Outcast install folder itself, or its GameData subfolder.",
                    "On Steam: right-click Jedi Outcast > Manage > Browse local files to find it.",
                ]),
                new("Steam must be closed for this operation",
                    "Steam rewrites its shortcuts when it exits, so the launcher can only add itself "
                    + "to Steam while Steam is not running.",
                [
                    "Quit Steam completely (Steam menu > Exit, not just the window), then try again.",
                    "If it is still detected, end Steam in the Task Manager.",
                ]),
                new("Steam profile not found",
                    "No Steam user profile could be located on this computer.",
                [
                    "Start Steam once and log in with your account, then try again.",
                    "If Steam is installed in an unusual place, set steam.path in launcher.properties.",
                ]),
                new("Login refused",
                    "The update server rejected the login.",
                [
                    "For a beta: check the beta code, it may be mistyped or the beta may have ended.",
                    "Switch back to the public channel in Options.",
                    "For the public channel: the server may be under maintenance, try again later.",
                ]),
                new("Could not start the game",
                    "Windows refused to run the game executable.",
                [
                    "Close any instance of the game that is already running, then try again.",
                    "Allow the game in your antivirus, it may be blocking it.",
                    "Run Options > Check integrity, then Update to repair damaged files.",
                ]),
            ]),
            ("Installation",
            [
                new("Jedi Academy not found",
                    "The launcher is not in a Jedi Academy folder, and the installation was cancelled. "
                    + "Galactic Legacy needs the original Jedi Academy files (base\\assets0.pk3 to assets2.pk3).",
                [
                    "Use Options > Install in a new folder... to install Galactic Legacy.",
                    "Or move the launcher into your Jedi Academy GameData folder and start it again.",
                ]),
                new("Jedi Academy was not found in this folder",
                    "The folder chosen in the installer does not contain the Jedi Academy archives "
                    + "(base\\assets0.pk3 to assets2.pk3).",
                [
                    "Select the Jedi Academy install folder itself, or its GameData subfolder.",
                    "On Steam: right-click Jedi Academy > Manage > Browse local files to find it.",
                    "CD or GOG version: look in Program Files (x86)\\LucasArts or in your GOG Games folder.",
                ]),
                new("Jedi Outcast was not found in this folder",
                    "The optional Jedi Outcast folder does not contain jk2sp.exe, jk2mp.exe and jk2gamex86.dll.",
                [
                    "Select the Jedi Outcast install folder itself, or its GameData subfolder.",
                    "Or leave the field empty: Jedi Outcast can be added later with Options > Configure Jedi Outcast.",
                ]),
                new("Choose a folder outside Program Files",
                    "Windows protects Program Files: without administrator rights the launcher could not "
                    + "update the game there.",
                [
                    "Pick a folder of your own, e.g. C:\\Games\\Star Wars Galactic Legacy.",
                ]),
                new("The install folder must be empty",
                    "The installer only writes into a new or empty folder, so it never mixes with or "
                    + "overwrites other files.",
                [
                    "Type the name of a folder that does not exist yet: it will be created.",
                    "Or empty the chosen folder first.",
                ]),
                new("The install folder cannot be inside the Jedi Academy folder",
                    "Galactic Legacy is installed next to Jedi Academy, never inside it, so your Jedi "
                    + "Academy installation stays untouched.",
                [
                    "Choose a folder outside the Jedi Academy folder.",
                ]),
                new("Not enough free space",
                    "The drive cannot hold the copied Jedi Academy files (about 1.2 GB), plus the mod files "
                    + "when an installation is moved.",
                [
                    "Free some space on the drive, then try again.",
                    "Or choose a folder on another drive.",
                ]),
                new("Installation failed",
                    "A file could not be copied. The files already copied are removed, and your current "
                    + "installation is not modified. The log (top-left button) shows the exact cause.",
                [
                    "Close the game and Steam, then try again.",
                    "Right-click the launcher > Run as administrator.",
                    "Check the free space on the destination drive.",
                ]),
            ]),
            ("Network",
            [
                new("Failed",
                    "The launcher could not reach the update server. The log (top-left button) shows "
                    + "the exact cause.",
                [
                    "Check your internet connection, then click Update again: downloads resume where they stopped.",
                    "Allow the launcher through your firewall, or disable your VPN or proxy.",
                    "If nothing works, the server may be down: ask on Discord.",
                ]),
                new("Reconnecting (1/3)",
                    "The connection dropped during a download. This is not an error yet: the launcher "
                    + "retries on its own.",
                [
                    "Wait: the download resumes automatically.",
                    "If it keeps failing, check the stability of your connection (Wi-Fi, VPN).",
                ]),
                new("Manifest not found or empty on the server",
                    "The server has no published version for the selected channel.",
                [
                    "Try again in a few minutes: a release may be uploading.",
                    "Switch to another channel in Options.",
                ]),
                new("Launcher update failed",
                    "The new launcher version could not be downloaded from GitHub. The current "
                    + "version keeps working.",
                [
                    "Check your connection and restart the launcher.",
                    "Download the latest launcher by hand from the mod page.",
                ]),
            ]),
            ("Files",
            [
                new("Access to the path is denied",
                    "The launcher cannot write to a file or folder: the game may be running, or the "
                    + "folder is protected by Windows.",
                [
                    "Close the game, then try again.",
                    "Right-click the launcher > Run as administrator.",
                    "Move the game out of Program Files to a folder you own (e.g. C:\\Games\\SWGL).",
                ]),
                new("The process cannot access the file",
                    "Another program is using the file, usually the game itself or an antivirus scan.",
                [
                    "Close the game, then try again.",
                    "Wait for your antivirus scan to finish, or add the game folder to its exclusions.",
                    "Restart your computer if the file stays locked.",
                ]),
                new("... failed its checksum after download",
                    "A downloaded file arrived damaged.",
                [
                    "Click Update again: the file is downloaded anew.",
                    "If it happens again, check your connection or your antivirus, which may alter the file.",
                ]),
                new("... file(s) could not be removed",
                    "During uninstall, some files were in use or protected.",
                [
                    "Close the game, then uninstall again.",
                    "Right-click the launcher > Run as administrator.",
                    "Check the log for the list of files and delete them by hand.",
                ]),
            ]),
        ];


        private readonly Color _accent;
        private readonly Rectangle _titleBounds;
        private readonly Font _titleFont = new("Segoe UI Semibold", 11f);
        private readonly Font _sectionFont = new("Segoe UI Semibold", 8.5f);
        private readonly Font _headerFont = new("Segoe UI Semibold", 10f);
        private readonly Font _answerFont = new("Segoe UI", 9.5f);
        private readonly FlowLayoutPanel _list;
        private readonly float _scale;
        private readonly int _cornerRadius;

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string? appName, string? idList);

        public FaqForm(Color accent, float scale)
        {
            _accent = accent;
            _scale = scale;
            _cornerRadius = Scaled(RoundedWindow.DesignRadius);

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            BackColor = PageBackColor;
            AutoScaleMode = AutoScaleMode.None;
            KeyPreview = true;
            ClientSize = new Size(Scaled(560), Scaled(480));
            Text = "Common errors";

            _titleBounds = new Rectangle(
                Scaled(24), Scaled(18), ClientSize.Width - Scaled(48), Scaled(26));

            int bottom = ClientSize.Height - Scaled(58);

            _list = new FlowLayoutPanel
            {
                Location = new Point(Scaled(24), Scaled(54)),
                Size = new Size(ClientSize.Width - Scaled(36), bottom - Scaled(54)),
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                BackColor = PageBackColor,
                Padding = Padding.Empty,
            };

            // Les lignes laissent toujours la place de la barre de defilement : elles ne
            // changent pas de largeur quand elle apparait au depliage d'une reponse.
            int rowWidth = _list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - Scaled(4);

            _list.SuspendLayout();

            foreach ((string section, Entry[] entries) in Sections)
            {
                _list.Controls.Add(CreateSection(section, rowWidth));

                foreach (Entry entry in entries)
                {
                    AddEntry(entry, rowWidth);
                }
            }

            _list.ResumeLayout();

            var close = new FlatButton
            {
                Text = "Close",
                AccentColor = accent,
                Primary = true,
                Size = new Size(Scaled(84), Scaled(28)),
                Location = new Point(ClientSize.Width - Scaled(24) - Scaled(84), bottom + Scaled(16)),
            };

            close.Click += (_, _) => Close();

            KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    Close();
                }
            };

            Controls.AddRange([_list, close]);
        }

        private int Scaled(int amount) => (int)Math.Round(amount * _scale);

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            RoundedWindow.Apply(this, _cornerRadius);

            // Barre de defilement sombre, comme celle du journal.
            _ = SetWindowTheme(_list.Handle, "DarkMode_Explorer", null);
        }

        /// <summary>Intitule de rubrique, en petites capitales grises.</summary>
        private Label CreateSection(string title, int width)
        {
            bool first = _list.Controls.Count == 0;

            return new Label
            {
                Text = title.ToUpperInvariant(),
                Font = _sectionFont,
                ForeColor = SectionColor,
                AutoSize = false,
                Size = new Size(width, Scaled(first ? 22 : 34)),
                TextAlign = ContentAlignment.BottomLeft,
                Margin = new Padding(0, 0, 0, Scaled(4)),
                Padding = new Padding(Scaled(2), 0, 0, Scaled(2)),
            };
        }

        /// <summary>
        /// Une erreur : ligne de titre cliquable, et sa reponse repliee dessous (la cause,
        /// puis une ligne par solution, precedee d'une fleche d'accent).
        /// </summary>
        private void AddEntry(Entry entry, int width)
        {
            int indent = Scaled(28);
            int arrowWidth = Scaled(20);

            var answer = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = Padding.Empty,
                Padding = new Padding(0, 0, 0, Scaled(10)),
                Visible = false,
            };

            answer.Controls.Add(new Label
            {
                Text = entry.Cause,
                Font = _answerFont,
                ForeColor = AnswerColor,
                AutoSize = true,
                MaximumSize = new Size(width, 0),
                MinimumSize = new Size(width, 0),
                Padding = new Padding(indent, Scaled(2), Scaled(8), Scaled(6)),
                Margin = Padding.Empty,
            });

            foreach (string solution in entry.Solutions)
            {
                var line = new Label
                {
                    Text = solution,
                    Font = _answerFont,
                    ForeColor = AnswerColor,
                    AutoSize = true,
                    MaximumSize = new Size(width, 0),
                    MinimumSize = new Size(width, 0),
                    Padding = new Padding(indent + arrowWidth, Scaled(1), Scaled(8), Scaled(3)),
                    Margin = Padding.Empty,
                };

                line.Paint += (_, e) => TextRenderer.DrawText(
                    e.Graphics, "→", _answerFont,
                    new Point(indent + Scaled(2), line.Padding.Top), _accent, TextFormatFlags.NoPadding);

                answer.Controls.Add(line);
            }

            var header = new Label
            {
                Text = entry.Error,
                Font = _headerFont,
                ForeColor = _accent,
                AutoSize = false,
                Size = new Size(width, Scaled(34)),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(indent, 0, Scaled(8), 0),
                Margin = Padding.Empty,
                Cursor = Cursors.Hand,
                AutoEllipsis = true,
            };

            header.MouseEnter += (_, _) => header.BackColor = HoverBackColor;
            header.MouseLeave += (_, _) => header.BackColor = PageBackColor;
            header.Click += (_, _) =>
            {
                answer.Visible = !answer.Visible;
                header.Invalidate();

                if (answer.Visible)
                {
                    _list.ScrollControlIntoView(answer);
                }
            };

            header.Paint += (_, e) =>
            {
                // Chevron a gauche, filet de separation au-dessus.
                var chevron = new Rectangle(Scaled(6), 0, indent - Scaled(6), header.Height);
                TextRenderer.DrawText(
                    e.Graphics, answer.Visible ? Expanded : Collapsed, _headerFont, chevron, _accent,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

                using var pen = new Pen(DividerColor);
                e.Graphics.DrawLine(pen, 0, 0, header.Width, 0);
            };

            _list.Controls.Add(header);
            _list.Controls.Add(answer);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            TextRenderer.DrawText(
                e.Graphics, Text, _titleFont, _titleBounds, _accent,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            RoundedWindow.DrawBorder(e.Graphics, this, Color.FromArgb(120, _accent), _cornerRadius);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _titleFont.Dispose();
                _sectionFont.Dispose();
                _headerFont.Dispose();
                _answerFont.Dispose();
            }

            base.Dispose(disposing);
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
