using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Media;
using System.Runtime.InteropServices;
using FluentFTP.Exceptions;

namespace SWGLLauncher
{
    /// <summary>
    /// Fenetre principale du launcher : sans bordure, image de fond et musique
    /// definies dans "launcher.properties", avec ses propres boutons de barre de titre
    /// et la barre de mise a jour (canal public ou beta).
    /// </summary>
    public partial class LauncherForm : Form
    {
        private const int WM_NCHITTEST = 0x0084;
        private const int WM_NCLBUTTONDBLCLK = 0x00A3;
        private const int HTCLIENT = 1;
        private const int HTCAPTION = 2;

        /// <summary>Au-dela, les lignes les plus anciennes du journal sont retirees.</summary>
        private const int MaxLogLines = 1000;

        // Gabarit de la barre basse, en pixels logiques (96 ppp).
        private const int BarHeight = 96;
        private const int BarPadding = 24;
        private const int StatusOffsetY = 12;
        private const int ProgressOffsetY = 40;
        private const int ProgressHeight = 6;
        private const int RowOffsetY = 56;
        private const int FieldHeight = 28;

        private const string PublicChannelLabel = "Public";

        /// <summary>Nom du raccourci cree dans Steam.</summary>
        private const string DefaultSteamShortcutName = "STAR WARS™: Galactic Legacy";

        /// <summary>
        /// Ce que le nettoyage ne touche jamais : le dossier du jeu et les fichiers de
        /// l'installation Jedi Academy d'origine, nommes un par un. Un executable ou une
        /// DLL qui ne figure pas ici n'est pas du jeu et sera donc supprime.
        /// </summary>
        private const string DefaultKeepPatterns =
            "base/**,EaxMan.dll,IFC22.dll,OpenAL32.dll,SWGLLauncher.exe,"
            + "jagamex86.dll,jamp.exe,jasp.exe,launcher.properties,version.inf";

        private static readonly Color MenuBackColor = Color.FromArgb(22, 22, 26);

        [DllImport("gdi32.dll", EntryPoint = "CreateRoundRectRgn")]
        private static extern IntPtr CreateRoundRectRgn(
            int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string? appName, string? idList);

        private readonly LauncherConfig _config;
        private readonly MusicPlayer _music = new();

        // Valeurs issues de la configuration, exprimees en pixels logiques (96 ppp).
        private int _designWidth;
        private int _designHeight;
        private int _cornerRadius;
        private int _buttonSize;
        private Color _accentColor = Color.FromArgb(232, 185, 35);

        // Canal courant : code de beta, ou chaine vide pour le canal public.
        private readonly List<string> _betaCodes = [];
        private string _selectedCode = string.Empty;

        private bool _musicEnabled;

        // Menus conserves d'un affichage a l'autre.
        private ContextMenuStrip? _optionsMenu;
        private ContextMenuStrip? _updateMenu;

        // Etat affiche dans la barre basse.
        private string _statusText = string.Empty;
        private string _detailText = string.Empty;
        private double _progressFraction = -1;

        private CancellationTokenSource? _syncCancellation;
        private bool _isSyncing;

        private int _logLineCount;

        public LauncherForm()
        {
            _config = LauncherConfig.Load();

            InitializeComponent();
            ApplyConfiguration();
        }

        /// <summary>Vrai si la mise a jour est activee et un serveur est renseigne.</summary>
        private bool SyncConfigured =>
            _config.GetBool("sync.enabled", true)
            && _config.GetString("ftp.host", string.Empty).Length > 0;

        private string InstallRoot => _config.ResolvePath(_config.GetString("install.path", "."));

        private string StatePath => _config.ResolvePath(_config.GetString("state.file", "installed.json"));

        private float DpiScale => DeviceDpi / 96f;

        /// <summary>
        /// Applique le contenu de "launcher.properties" a la fenetre.
        /// </summary>
        private void ApplyConfiguration()
        {
            Text = _config.GetString("window.title", "SWGL Launcher");

            _designWidth = Math.Max(320, _config.GetInt("window.width", 960));
            _designHeight = Math.Max(180, _config.GetInt("window.height", 540));
            _cornerRadius = Math.Max(0, _config.GetInt("window.corner.radius", 14));
            _buttonSize = Math.Max(16, _config.GetInt("titlebar.button.size", 34));

            BackgroundImage = LoadBackgroundImage();

            _accentColor = _config.GetColor("ui.accent.color", Color.FromArgb(232, 185, 35));
            Color glyph = _config.GetColor("titlebar.button.color", _accentColor);
            Color glyphHover = _config.GetColor("titlebar.button.hover.color", Color.White);
            Color hoverBack = _config.GetColor(
                "titlebar.button.hover.background", Color.FromArgb(64, 255, 255, 255));
            Color closeHoverBack = _config.GetColor(
                "titlebar.close.hover.background", Color.FromArgb(192, 192, 48, 48));

            foreach (TitleBarButton button in new[] { btnLog, btnMusic, btnMinimize, btnClose })
            {
                button.GlyphColor = glyph;
                button.HoverGlyphColor = glyphHover;
                button.HoverBackColor = hoverBack;
            }

            btnClose.HoverBackColor = closeHoverBack;

            foreach (FlatButton button in new[] { btnOptions, btnUpdate, btnStart })
            {
                button.AccentColor = _accentColor;
            }

            _musicEnabled = _config.GetBool("music.enabled", true);
            btnMusic.Glyph = _musicEnabled ? TitleBarGlyph.SoundOn : TitleBarGlyph.SoundOff;

            ApplyWindowIcon();

            LoadChannels();
            InitializeStatusFromInstalledState();
        }

        /// <summary>
        /// Reprend l'icone de l'executable pour la barre des taches : la fenetre n'ayant
        /// pas de bordure, c'est le seul endroit ou elle apparait.
        /// </summary>
        private void ApplyWindowIcon()
        {
            try
            {
                string executable = Environment.ProcessPath ?? Application.ExecutablePath;
                Icon = Icon.ExtractAssociatedIcon(executable);
            }
            catch
            {
                // Pas d'icone dans l'executable : WinForms garde la sienne.
            }
        }

        /// <summary>Recharge la liste des betas connues et le canal selectionne.</summary>
        private void LoadChannels()
        {
            _betaCodes.Clear();
            _betaCodes.AddRange(_config
                .GetString("beta.codes", string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase));

            _selectedCode = _config.GetString("channel.selected", string.Empty).Trim();

            if (_selectedCode.Length > 0 && !_betaCodes.Contains(_selectedCode, StringComparer.OrdinalIgnoreCase))
            {
                _betaCodes.Add(_selectedCode);
            }
        }

        /// <summary>Libelle du canal courant, tel qu'affiche dans le menu et la barre d'etat.</summary>
        private string CurrentChannelLabel =>
            _selectedCode.Length == 0 ? PublicChannelLabel : $"Beta {_selectedCode}";

        /// <summary>
        /// Affiche l'etat de l'installation deja presente sur le disque.
        /// </summary>
        private void InitializeStatusFromInstalledState()
        {
            InstalledState state = ManifestService.LoadInstalledState(StatePath);

            if (!SyncConfigured)
            {
                _statusText = "Updates are not configured";
                _detailText = "Set ftp.host in launcher.properties";
                btnUpdate.Enabled = false;
                return;
            }

            _statusText = state.Files.Count > 0 ? "Installation found" : "No installation found";
            _detailText = DescribeChannel(state.Channel, state.Version);
        }

        /// <summary>
        /// Charge l'image de fond en memoire : le fichier n'est pas verrouille et reste
        /// remplacable pendant que le launcher tourne.
        /// </summary>
        private Image? LoadBackgroundImage()
        {
            try
            {
                byte[]? bytes = Assets.ReadAllBytes(_config, "background.image", "SWGL/background.png");

                if (bytes is null)
                {
                    return null;
                }

                // Copie en memoire : ni le fichier ni la ressource ne restent ouverts.
                using var stream = new MemoryStream(bytes);
                return Image.FromStream(stream);
            }
            catch
            {
                // Image illisible : la fenetre restera sur sa couleur de fond.
                return null;
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ApplyScaledLayout();

            // Barre de defilement sombre, assortie au journal (Windows 10 1809 et suivants ;
            // sans effet ailleurs).
            _ = SetWindowTheme(txtLog.Handle, "DarkMode_Explorer", null);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            if (_musicEnabled)
            {
                StartMusic();
            }

            if (SyncConfigured && _config.GetBool("sync.check.on.start", true))
            {
                // Simple etat des lieux au demarrage : rien n'est telecharge sans action
                // de l'utilisateur.
                _ = RunSyncAsync(applyChanges: false, fullVerify: false);
            }
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            ApplyScaledLayout();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _syncCancellation?.Cancel();
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _music.Dispose();
            _optionsMenu?.Dispose();
            _updateMenu?.Dispose();
            BackgroundImage?.Dispose();
            BackgroundImage = null;
            base.OnFormClosed(e);
        }

        private void StartMusic()
        {
            if (Assets.Open(_config, "music.file", "SWGL/music.mp3") is not (Stream content, string extension))
            {
                return;
            }

            _music.Play(
                content,
                extension,
                _config.GetBool("music.loop", true),
                _config.GetInt("music.volume", 60));
        }

        private void BtnMusic_Click(object? sender, EventArgs e)
        {
            _musicEnabled = !_musicEnabled;

            if (_musicEnabled)
            {
                StartMusic();
            }
            else
            {
                _music.Stop();
            }

            btnMusic.Glyph = _musicEnabled ? TitleBarGlyph.SoundOn : TitleBarGlyph.SoundOff;
            btnMusic.Invalidate();

            // Le choix est memorise dans launcher.properties.
            _config.Set("music.enabled", _musicEnabled ? "true" : "false");
        }

        // ------------------------------------------------------------------
        // Menus
        // ------------------------------------------------------------------

        /// <summary>Applique l'habillage sombre a un menu et a ses sous-menus.</summary>
        private ContextMenuStrip CreateMenu()
        {
            return new ContextMenuStrip
            {
                Renderer = new DarkMenuRenderer(_accentColor),
                BackColor = MenuBackColor,
                ForeColor = _accentColor,
                Font = new Font("Segoe UI", 9f),

                // La marge d'icone sert a afficher la coche du canal actif.
                ShowImageMargin = true,
            };
        }

        /// <summary>Vide un menu en liberant ses entrees precedentes.</summary>
        private static void ClearItems(ToolStripDropDown menu)
        {
            ToolStripItem[] previous = [.. menu.Items.Cast<ToolStripItem>()];
            menu.Items.Clear();

            foreach (ToolStripItem item in previous)
            {
                item.Dispose();
            }
        }

        private void BtnOptions_Click(object? sender, EventArgs e)
        {
            // Les menus sont conserves d'un affichage a l'autre : les detruire depuis leur
            // propre evenement Closed ferait planter la boucle de messages WinForms.
            _optionsMenu ??= CreateMenu();
            ContextMenuStrip menu = _optionsMenu;
            ClearItems(menu);

            var channelItem = new ToolStripMenuItem("Beta channel");
            channelItem.DropDown.Renderer = new DarkMenuRenderer(_accentColor);
            channelItem.DropDown.BackColor = MenuBackColor;
            channelItem.DropDown.ForeColor = _accentColor;
            channelItem.DropDownItems.Add(CreateChannelItem(PublicChannelLabel, string.Empty));

            foreach (string code in _betaCodes)
            {
                channelItem.DropDownItems.Add(CreateChannelItem($"Beta {code}", code));
            }

            channelItem.DropDownItems.Add(new ToolStripSeparator());

            var addItem = new ToolStripMenuItem("Add beta code...");
            addItem.Click += (_, _) => PromptForBetaCode();
            channelItem.DropDownItems.Add(addItem);

            var removeItem = new ToolStripMenuItem("Remove this beta")
            {
                Enabled = _selectedCode.Length > 0,
            };
            removeItem.Click += (_, _) => RemoveSelectedBeta();
            channelItem.DropDownItems.Add(removeItem);

            menu.Items.Add(channelItem);
            menu.Items.Add(new ToolStripSeparator());

            var jediOutcastItem = new ToolStripMenuItem("Configure Jedi Outcast...");
            jediOutcastItem.Click += (_, _) => ConfigureJediOutcast();
            menu.Items.Add(jediOutcastItem);

            var steamItem = new ToolStripMenuItem("Configure Steam launcher...");
            steamItem.Click += (_, _) => ConfigureSteamLauncher();
            menu.Items.Add(steamItem);

            // Le bouton est en bas de la fenetre : le menu s'ouvre vers le haut.
            menu.Show(btnOptions, new Point(0, 0), ToolStripDropDownDirection.AboveRight);
        }

        private void BtnUpdateDropDown_Click(object? sender, EventArgs e)
        {
            _updateMenu ??= CreateMenu();
            ContextMenuStrip menu = _updateMenu;
            ClearItems(menu);

            var verifyItem = new ToolStripMenuItem("Verify only")
            {
                Enabled = !_isSyncing && SyncConfigured,
                ToolTipText = "Recompute every checksum and report what differs, without downloading.",
            };

            verifyItem.Click += (_, _) => _ = RunSyncAsync(applyChanges: false, fullVerify: true);
            menu.Items.Add(verifyItem);

            menu.Show(btnUpdate, new Point(btnUpdate.Width, 0), ToolStripDropDownDirection.AboveLeft);
        }

        private ToolStripMenuItem CreateChannelItem(string label, string code)
        {
            var item = new ToolStripMenuItem(label)
            {
                Checked = code.Equals(_selectedCode, StringComparison.OrdinalIgnoreCase),
                CheckOnClick = false,
            };

            item.Click += (_, _) => SelectChannel(code);
            return item;
        }

        private void SelectChannel(string code)
        {
            if (_isSyncing)
            {
                return;
            }

            _selectedCode = code;
            _config.Set("channel.selected", code);
            SetStatus($"Channel: {CurrentChannelLabel}", string.Empty);

            if (SyncConfigured)
            {
                _ = RunSyncAsync(applyChanges: false, fullVerify: false);
            }
        }

        private void PromptForBetaCode()
        {
            using var prompt = new TextPromptForm(
                _accentColor,
                DpiScale,
                "Beta code",
                "Enter the code you received. Pick \"Public\" in the menu to go back to the public release.",
                "OK",
                "Cancel",
                new PromptField());

            if (prompt.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            string code = prompt.Value;

            if (!_betaCodes.Contains(code, StringComparer.OrdinalIgnoreCase))
            {
                _betaCodes.Add(code);
                SaveBetaCodes();
            }

            SelectChannel(code);
        }

        private void RemoveSelectedBeta()
        {
            if (_selectedCode.Length == 0)
            {
                return;
            }

            _betaCodes.RemoveAll(code => code.Equals(_selectedCode, StringComparison.OrdinalIgnoreCase));
            SaveBetaCodes();
            SelectChannel(string.Empty);
        }

        private void SaveBetaCodes() => _config.Set("beta.codes", string.Join(',', _betaCodes));

        // ------------------------------------------------------------------
        // Configuration depuis le menu Options
        // ------------------------------------------------------------------

        /// <summary>
        /// Demande le dossier de Jedi Outcast, le valide, puis importe ses assets
        /// dans le dossier "base" de SWGL.
        /// </summary>
        private void ConfigureJediOutcast()
        {
            using var prompt = new TextPromptForm(
                _accentColor,
                DpiScale,
                "Jedi Outcast",
                "Select your Jedi Outcast folder. Assets0.pk3, Assets1.pk3 and Assets2.pk3 will be "
                    + $"copied into the SWGL base folder, prefixed with {JediOutcastService.Prefix}.",
                "Import",
                "Cancel",
                new PromptField(
                    "Jedi Outcast folder",
                    _config.GetString("jo.path", string.Empty),
                    PromptBrowse.Folder));

            if (prompt.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            // Le dossier choisi peut etre la racine du jeu ou directement son GameData.
            string? gameData = JediOutcastService.ResolveGameData(prompt.Value);

            if (gameData is null)
            {
                SetStatus(
                    "Wrong folder selected",
                    "jk2sp.exe, jk2mp.exe and jk2gamex86.dll were not found");
                return;
            }

            _config.Set("jo.path", gameData);
            _ = ImportJediOutcastAsync(gameData);
        }

        private async Task ImportJediOutcastAsync(string jediOutcastGameData)
        {
            if (_isSyncing)
            {
                return;
            }

            SetSyncing(true);
            _syncCancellation = new CancellationTokenSource();
            IProgress<SyncProgress> progress = CreateProgress();

            try
            {
                string target = JediOutcastService.ResolveTargetBase(InstallRoot);

                int copied = await JediOutcastService.ImportAssetsAsync(
                    jediOutcastGameData, target, progress, _syncCancellation.Token);

                progress.Report(new SyncProgress(
                    "Jedi Outcast assets imported", $"{copied} files in {target}", 1));
            }
            catch (OperationCanceledException)
            {
                progress.Report(new SyncProgress("Cancelled", string.Empty, 0));
            }
            catch (Exception exception)
            {
                LogError("Jedi Outcast import", exception);
                progress.Report(new SyncProgress("Import failed", exception.Message, 0));
            }
            finally
            {
                _syncCancellation?.Dispose();
                _syncCancellation = null;
                SetSyncing(false);
            }
        }

        /// <summary>
        /// Ajoute SWGL aux jeux non-Steam (ecriture dans shortcuts.vdf du profil Steam).
        /// </summary>
        private void ConfigureSteamLauncher()
        {
            // Steam garde ses raccourcis en memoire et reecrit le fichier en quittant :
            // ecrire pendant qu'il tourne ne servirait a rien.
            if (SteamShortcutsService.IsSteamRunning())
            {
                SystemSounds.Hand.Play();
                SetStatus("Steam must be closed for this operation", string.Empty);
                return;
            }

            // Le raccourci vise toujours le launcher lui-meme, demarre depuis le dossier
            // du jeu : c'est le launcher qui met a jour puis lance SWGL.
            string target = Environment.ProcessPath ?? Application.ExecutablePath;

            if (!File.Exists(target))
            {
                SystemSounds.Hand.Play();
                SetStatus("Launcher executable not found", target);
                return;
            }

            string? shortcutsFile = SteamShortcutsService.FindShortcutsFile(
                _config.GetString("steam.shortcuts.file", string.Empty),
                _config.GetString("steam.path", string.Empty));

            if (shortcutsFile is null)
            {
                SystemSounds.Hand.Play();
                SetStatus("Steam profile not found", "Set steam.path in launcher.properties");
                return;
            }

            string name = _config.GetString("steam.shortcut.name", DefaultSteamShortcutName);

            try
            {
                SteamShortcutsService.Result result =
                    SteamShortcutsService.AddOrUpdate(shortcutsFile, name, target, InstallRoot);

                int artwork = InstallSteamArtwork(shortcutsFile, result.AppId);

                SetStatus(
                    result.Created ? "Added to Steam" : "Steam shortcut updated",
                    $"{name} — {artwork} artwork file(s)");
            }
            catch (Exception exception)
            {
                SystemSounds.Hand.Play();
                SetStatus("Could not update Steam", exception.Message);
                LogError("Steam shortcut", exception);
            }
        }

        /// <summary>
        /// Depose les visuels de la fiche Steam (capsules, logo, banniere) dans le dossier
        /// "grid" du profil. Un visuel non configure ou absent est simplement ignore.
        /// </summary>
        /// <returns>Le nombre de fichiers deposes.</returns>
        private int InstallSteamArtwork(string shortcutsFile, uint appId)
        {
            (SteamShortcutsService.SteamArtwork Kind, string Key, string Default)[] artwork =
            [
                (SteamShortcutsService.SteamArtwork.Cover, "steam.image.cover", "SWGL/cover.png"),
                (SteamShortcutsService.SteamArtwork.WideCover, "steam.image.wide", "SWGL/wide_cover.jpg"),
                (SteamShortcutsService.SteamArtwork.Logo, "steam.image.logo", "SWGL/logo.png"),
                (SteamShortcutsService.SteamArtwork.Hero, "steam.image.hero", string.Empty),
            ];

            int installed = 0;

            foreach ((SteamShortcutsService.SteamArtwork kind, string key, string fallback) in artwork)
            {
                if (Assets.Open(_config, key, fallback) is not (Stream content, string extension))
                {
                    continue;
                }

                try
                {
                    using (content)
                    {
                        SteamShortcutsService.InstallArtwork(
                            shortcutsFile, appId, kind, content, extension);
                    }

                    installed++;
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                    // Un visuel qui ne passe pas ne doit pas invalider le raccourci lui-meme.
                    Log($"Steam artwork {kind} skipped: {exception.Message}");
                }
            }

            return installed;
        }

        /// <summary>Demande l'autorisation de fermer puis relancer Steam.</summary>
        private bool ConfirmSteamRestart()
        {
            using var confirm = new TextPromptForm(
                _accentColor,
                DpiScale,
                "Steam is running",
                "Steam rewrites its shortcut file when it exits, so the shortcut can only be "
                    + "added while it is closed. The launcher can close Steam, add the shortcut, "
                    + "then start Steam again.",
                "Close Steam and continue",
                "Cancel");

            return confirm.ShowDialog(this) == DialogResult.OK;
        }

        // ------------------------------------------------------------------
        // Lancement du jeu
        // ------------------------------------------------------------------

        private void BtnStart_Click(object? sender, EventArgs e)
        {
            if (_isSyncing)
            {
                return;
            }

            string executable = _config.GetString("game.executable", "SWGL_SP.x86_64.exe");
            string path = Path.IsPathRooted(executable)
                ? executable
                : Path.Combine(InstallRoot, executable);

            if (!File.Exists(path))
            {
                SetStatus("Game not found", path);
                return;
            }

            try
            {
                var startInfo = new ProcessStartInfo(path)
                {
                    WorkingDirectory = Path.GetDirectoryName(path) ?? InstallRoot,
                    Arguments = _config.GetString("game.arguments", string.Empty),
                    UseShellExecute = true,
                };

                Process.Start(startInfo);
            }
            catch (Exception exception)
            {
                SetStatus("Could not start the game", exception.Message);
                LogError("game start", exception);
                return;
            }

            if (_config.GetBool("game.close.launcher", true))
            {
                Close();
                return;
            }

            SetStatus("Game started", Path.GetFileName(path));
        }

        // ------------------------------------------------------------------
        // Mise a jour
        // ------------------------------------------------------------------

        /// <summary>
        /// Compare l'installation locale au manifeste du canal choisi, et applique
        /// les differences si demande.
        /// </summary>
        /// <param name="applyChanges">Faux pour un simple etat des lieux.</param>
        /// <param name="fullVerify">Vrai pour recalculer toutes les empreintes.</param>
        private async Task RunSyncAsync(bool applyChanges, bool fullVerify)
        {
            if (_isSyncing)
            {
                return;
            }

            string? betaCode = _selectedCode.Length == 0 ? null : _selectedCode;

            SetSyncing(true);
            _syncCancellation = new CancellationTokenSource();
            CancellationToken token = _syncCancellation.Token;

            IProgress<SyncProgress> progress = CreateProgress();

            string mode = !applyChanges ? (fullVerify ? "Verify only" : "Check") : "Update";
            string host = _config.GetString("ftp.host", string.Empty);
            Log($"{mode} — channel {CurrentChannelLabel}, server {host}:{_config.GetInt("ftp.port", 21)}");

            try
            {
                progress.Report(new SyncProgress("Connecting to the server...", string.Empty));

                await using var ftp = new FtpClientService(_config) { Log = Log };
                await ftp.ConnectAsync(betaCode, token);
                Log($"Connected as {ftp.UserName}");

                progress.Report(new SyncProgress("Reading the manifest...", ftp.UserName));
                Manifest manifest = await ftp.DownloadManifestAsync(token);
                Log($"Manifest: {DescribeChannel(manifest.Channel, manifest.Version)}, {manifest.Files.Count} file(s)");

                var engine = new SyncEngine(InstallRoot, StatePath)
                {
                    BetaCode = betaCode ?? string.Empty,
                    RemoveUnknown = _config.GetBool("sync.remove.unknown", true),
                    Protected = BuildProtectedPaths(),
                    KeepPatterns = _config
                        .GetString("sync.keep", DefaultKeepPatterns)
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    DownloadRetries = Math.Max(0, _config.GetInt("sync.download.retries", 3)),
                    Log = Log,
                };

                SyncPlan plan = await engine.BuildPlanAsync(manifest, fullVerify, progress, token);
                Log($"Plan: {plan.Download.Count} to download ({SyncEngine.FormatSize(plan.TotalBytes)}), "
                    + $"{plan.Delete.Count} to remove, {plan.Keep.Count} up to date");

                if (plan.IsUpToDate)
                {
                    progress.Report(new SyncProgress(
                        "Up to date",
                        DescribeChannel(manifest.Channel, manifest.Version),
                        1));
                    return;
                }

                if (!applyChanges)
                {
                    var parts = new List<string>();

                    if (plan.Download.Count > 0)
                    {
                        parts.Add($"{plan.Download.Count} to download ({SyncEngine.FormatSize(plan.TotalBytes)})");
                    }

                    if (plan.Delete.Count > 0)
                    {
                        parts.Add($"{plan.Delete.Count} to remove");
                    }

                    string summary = string.Join("  —  ", parts);

                    progress.Report(new SyncProgress("Update available", summary, 0));
                    return;
                }

                await engine.ApplyAsync(plan, manifest, ftp, progress, token);

                Log("Update complete");
                progress.Report(new SyncProgress(
                    "Update complete",
                    DescribeChannel(manifest.Channel, manifest.Version),
                    1));
            }
            catch (OperationCanceledException)
            {
                Log("Cancelled");
                progress.Report(new SyncProgress("Cancelled", string.Empty, 0));
            }
            catch (FtpAuthenticationException exception)
            {
                LogError("login refused", exception);
                progress.Report(new SyncProgress(
                    "Login refused", "Invalid beta code, or the beta is closed", 0));
            }
            catch (Exception exception)
            {
                LogError(mode.ToLowerInvariant(), exception);
                progress.Report(new SyncProgress("Failed", exception.Message, 0));
            }
            finally
            {
                _syncCancellation?.Dispose();
                _syncCancellation = null;
                SetSyncing(false);
            }
        }

        private void SetSyncing(bool syncing)
        {
            _isSyncing = syncing;

            btnUpdate.Text = syncing ? "Cancel" : "Update";

            // Pendant une mise a jour, les fichiers du jeu bougent : on ne lance pas.
            btnStart.Enabled = !syncing;

            if (!syncing)
            {
                _progressFraction = _progressFraction >= 1 ? 1 : -1;
            }

            InvalidateStatusArea();
        }

        /// <summary>
        /// Fichiers que le nettoyage doit epargner quoi qu'il arrive : le launcher lui-meme,
        /// sa configuration, son etat, et un eventuel habillage depose a cote de l'exe.
        /// </summary>
        private IReadOnlyCollection<string> BuildProtectedPaths()
        {
            var paths = new List<string>();
            string root = Path.GetFullPath(InstallRoot);

            void Protect(string absolutePath)
            {
                string relative = Path.GetRelativePath(root, absolutePath);

                // Hors du dossier d'installation : le nettoyage ne l'atteindra jamais.
                if (Path.IsPathRooted(relative) || relative.StartsWith("..", StringComparison.Ordinal))
                {
                    return;
                }

                paths.Add(ManifestService.NormalizeRelativePath(relative));
            }

            Protect(Environment.ProcessPath ?? Application.ExecutablePath);
            Protect(_config.FilePath);
            Protect(StatePath);

            // L'habillage est embarque dans l'exe, mais un fichier pose a cote le remplace :
            // s'il existe, il ne doit pas disparaitre au premier nettoyage.
            foreach ((string key, string fallback) in new[]
            {
                ("background.image", "SWGL/background.png"),
                ("music.file", "SWGL/music.mp3"),
                ("steam.image.cover", "SWGL/cover.png"),
                ("steam.image.wide", "SWGL/wide_cover.jpg"),
                ("steam.image.logo", "SWGL/logo.png"),
                ("steam.image.hero", string.Empty),
            })
            {
                string configured = _config.GetString(key, fallback);

                if (configured.Length == 0)
                {
                    continue;
                }

                string resolved = _config.ResolvePath(configured);

                if (File.Exists(resolved))
                {
                    Protect(resolved);
                }
            }

            return paths;
        }

        /// <summary>
        /// Rapporteur d'avancement des operations longues : il revient sur le fil de
        /// l'interface, puisqu'il est cree depuis celui-ci.
        /// </summary>
        private IProgress<SyncProgress> CreateProgress()
        {
            return new Progress<SyncProgress>(report =>
            {
                _statusText = report.Status;
                _detailText = report.Detail;
                _progressFraction = report.Fraction;
                InvalidateStatusArea();
            });
        }

        /// <summary>Met a jour les deux lignes de la barre d'etat, et les note au journal.</summary>
        private void SetStatus(string status, string detail)
        {
            _statusText = status;
            _detailText = detail;
            _progressFraction = -1;
            InvalidateStatusArea();

            Log(detail.Length > 0 ? $"{status} — {detail}" : status);
        }

        // ------------------------------------------------------------------
        // Journal
        // ------------------------------------------------------------------

        private void BtnLog_Click(object? sender, EventArgs e) => SetLogVisible(!pnlLog.Visible);

        private void SetLogVisible(bool visible)
        {
            pnlLog.Visible = visible;
            btnLog.Glyph = visible ? TitleBarGlyph.LogHide : TitleBarGlyph.LogShow;
            btnLog.Invalidate();

            if (visible)
            {
                pnlLog.BringToFront();
                txtLog.SelectionStart = txtLog.TextLength;
                txtLog.ScrollToCaret();
            }
        }

        /// <summary>
        /// Ajoute une ligne horodatee au journal. Appelable depuis n'importe quel fil :
        /// FluentFTP journalise depuis ses propres fils.
        /// </summary>
        private void Log(string message)
        {
            if (IsDisposed || Disposing)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(() => Log(message));
                return;
            }

            string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            txtLog.AppendText(txtLog.TextLength == 0 ? line : Environment.NewLine + line);

            // Taille bornee : on ne retaille que de temps en temps, relire toutes les
            // lignes a chaque ajout couterait cher pendant un gros telechargement.
            if (++_logLineCount > MaxLogLines + 200)
            {
                txtLog.Lines = txtLog.Lines[^MaxLogLines..];
                _logLineCount = MaxLogLines;
                txtLog.SelectionStart = txtLog.TextLength;
                txtLog.ScrollToCaret();
            }
        }

        /// <summary>
        /// Journalise une erreur avec ses causes, et ouvre le journal pour qu'elle se voie.
        /// </summary>
        private void LogError(string context, Exception exception)
        {
            Log($"ERROR {context}: {exception.GetType().Name}: {exception.Message}");

            for (Exception? inner = exception.InnerException; inner is not null; inner = inner.InnerException)
            {
                Log($"    caused by {inner.GetType().Name}: {inner.Message}");
            }

            SetLogVisible(true);
        }

        /// <summary>Filet d'accent autour du journal.</summary>
        private void PnlLog_Paint(object? sender, PaintEventArgs e)
        {
            using var pen = new Pen(Color.FromArgb(120, _accentColor));
            e.Graphics.DrawRectangle(pen, 0, 0, pnlLog.Width - 1, pnlLog.Height - 1);
        }

        private static string DescribeChannel(string channel, string version)
        {
            if (channel.Length == 0)
            {
                return string.Empty;
            }

            return version.Length > 0 ? $"{channel} — {version}" : channel;
        }

        private void BtnUpdate_Click(object? sender, EventArgs e)
        {
            if (_isSyncing)
            {
                _syncCancellation?.Cancel();
                return;
            }

            _ = RunSyncAsync(applyChanges: true, fullVerify: false);
        }

        // ------------------------------------------------------------------
        // Mise en page et rendu
        // ------------------------------------------------------------------

        /// <summary>
        /// Met la taille de la fenetre et des controles a l'echelle du ppp courant,
        /// puis regenere les coins arrondis.
        /// </summary>
        private void ApplyScaledLayout()
        {
            int Scaled(int value) => (int)Math.Round(value * DpiScale);

            ClientSize = new Size(Scaled(_designWidth), Scaled(_designHeight));

            int size = Scaled(_buttonSize);
            int margin = Scaled(8);
            int half = margin / 2;

            btnClose.Size = new Size(size, size);
            btnClose.Location = new Point(ClientSize.Width - size - margin, margin);

            btnMinimize.Size = new Size(size, size);
            btnMinimize.Location = new Point(btnClose.Left - size - half, margin);

            btnMusic.Size = new Size(size, size);
            btnMusic.Location = new Point(btnMinimize.Left - size - half, margin);

            int rowY = ClientSize.Height - Scaled(BarHeight) + Scaled(RowOffsetY);
            int fieldHeight = Scaled(FieldHeight);
            int padding = Scaled(BarPadding);
            int gap = Scaled(12);

            // A gauche : Options. A droite : Update (avec son chevron) puis Start.
            btnOptions.Size = new Size(Scaled(140), fieldHeight);
            btnOptions.Location = new Point(padding, rowY);

            btnStart.Size = new Size(Scaled(130), fieldHeight);
            btnStart.Location = new Point(
                ClientSize.Width - padding - btnStart.Width, rowY);

            btnUpdate.Size = new Size(Scaled(170), fieldHeight);
            btnUpdate.Location = new Point(
                btnStart.Left - gap - btnUpdate.Width, rowY);

            // Journal : bouton en haut a gauche, en miroir des boutons de la barre de titre,
            // panneau sur la gauche entre la barre de titre et la barre basse.
            btnLog.Size = new Size(size, size);
            btnLog.Location = new Point(margin, margin);

            int logTop = btnLog.Bottom + margin;
            int logBottom = ClientSize.Height - Scaled(BarHeight) - margin;

            // 320 de large : le panneau s'arrete juste avant le logo centre en haut.
            pnlLog.Location = new Point(padding, logTop);
            pnlLog.Size = new Size(Scaled(320), Math.Max(Scaled(80), logBottom - logTop));
            pnlLog.Padding = new Padding(Scaled(10), Scaled(8), Scaled(4), Scaled(8));

            ApplyRoundedCorners(Scaled(_cornerRadius));
            Invalidate(true);
        }

        private void ApplyRoundedCorners(int radius)
        {
            Region?.Dispose();

            if (radius <= 0)
            {
                Region = null;
                return;
            }

            IntPtr handle = CreateRoundRectRgn(
                0, 0, Width + 1, Height + 1, radius * 2, radius * 2);

            // Region.FromHrgn copie la region : le handle GDI d'origine doit etre libere.
            Region = Region.FromHrgn(handle);
            _ = DeleteObject(handle);
        }

        private Rectangle StatusArea
        {
            get
            {
                int barHeight = (int)Math.Round(BarHeight * DpiScale);
                return new Rectangle(
                    0, ClientSize.Height - barHeight, ClientSize.Width, barHeight);
            }
        }

        private void InvalidateStatusArea() => Invalidate(StatusArea);

        protected override void OnPaintBackground(PaintEventArgs e) => PaintCanvas(e.Graphics);

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            PaintStatusBar(e.Graphics);
        }

        /// <summary>
        /// Dessine, pour un controle enfant, la portion de fond situee derriere lui :
        /// les boutons apparaissent ainsi transparents, bandeau compris.
        /// </summary>
        internal void PaintBackgroundSlice(Control child, Graphics graphics)
        {
            GraphicsState state = graphics.Save();
            graphics.TranslateTransform(-child.Left, -child.Top);
            PaintCanvas(graphics);
            graphics.Restore(state);
        }

        /// <summary>Image de fond puis bandeau sombre, dans le repere de la fenetre.</summary>
        private void PaintCanvas(Graphics graphics)
        {
            if (BackgroundImage is null)
            {
                using var background = new SolidBrush(BackColor);
                graphics.FillRectangle(background, 0, 0, ClientSize.Width, ClientSize.Height);
            }
            else
            {
                graphics.DrawImage(
                    BackgroundImage, 0, 0, ClientSize.Width, ClientSize.Height);
            }

            Rectangle bar = StatusArea;
            if (bar.Height <= 0)
            {
                return;
            }

            using var gradient = new LinearGradientBrush(
                new Rectangle(bar.X, bar.Y - 1, bar.Width, bar.Height + 1),
                Color.FromArgb(0, 0, 0, 0),
                Color.FromArgb(225, 0, 0, 0),
                LinearGradientMode.Vertical);

            graphics.FillRectangle(gradient, bar);
        }

        /// <summary>Texte d'etat et barre de progression, dessines par-dessus le bandeau.</summary>
        private void PaintStatusBar(Graphics graphics)
        {
            int Scaled(int value) => (int)Math.Round(value * DpiScale);

            Rectangle bar = StatusArea;
            int padding = Scaled(BarPadding);
            int width = ClientSize.Width - (padding * 2);

            if (width <= 0)
            {
                return;
            }

            using var font = new Font("Segoe UI", 9f);

            var statusRect = new Rectangle(
                padding, bar.Y + Scaled(StatusOffsetY), (int)(width * 0.5f), Scaled(18));

            TextRenderer.DrawText(
                graphics, _statusText, font, statusRect, _accentColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            var detailRect = new Rectangle(
                padding + (int)(width * 0.5f), statusRect.Y, (int)(width * 0.5f), Scaled(18));

            TextRenderer.DrawText(
                graphics, _detailText, font, detailRect, Color.FromArgb(200, 210, 210, 215),
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            // Piste de la barre de progression.
            var track = new Rectangle(
                padding, bar.Y + Scaled(ProgressOffsetY), width, Scaled(ProgressHeight));

            using (var trackBrush = new SolidBrush(Color.FromArgb(60, 255, 255, 255)))
            {
                graphics.FillRectangle(trackBrush, track);
            }

            if (_progressFraction < 0)
            {
                return;
            }

            int filled = (int)Math.Round(track.Width * Math.Clamp(_progressFraction, 0, 1));
            if (filled <= 0)
            {
                return;
            }

            using var fill = new SolidBrush(_accentColor);
            graphics.FillRectangle(fill, track.X, track.Y, filled, track.Height);
        }

        /// <summary>
        /// Rend toute la surface de la fenetre equivalente a une barre de titre :
        /// la fenetre sans bordure se deplace au cliquer-glisser.
        /// </summary>
        protected override void WndProc(ref Message m)
        {
            // Consequence de la "barre de titre" etendue a toute la fenetre : Windows
            // traiterait un double-clic sur le fond comme une demande d'agrandissement.
            if (m.Msg == WM_NCLBUTTONDBLCLK)
            {
                m.Result = IntPtr.Zero;
                return;
            }

            base.WndProc(ref m);

            if (m.Msg == WM_NCHITTEST && m.Result == HTCLIENT)
            {
                m.Result = HTCAPTION;
            }
        }

        private void BtnMinimize_Click(object? sender, EventArgs e)
        {
            WindowState = FormWindowState.Minimized;
        }

        private void BtnClose_Click(object? sender, EventArgs e)
        {
            Close();
        }
    }
}
