using System.Diagnostics;

namespace SWGLLauncher
{
    // ------------------------------------------------------------------
    // Installation propre : verification du dossier au demarrage, assistant
    // d'installation dans un dossier dedie, et retrait de l'ancien launcher.
    // ------------------------------------------------------------------
    public partial class LauncherForm
    {
        /// <summary>Etat cree dans le dossier d'une installation propre.</summary>
        private const string InstalledStateFileName = "installed.json";

        /// <summary>Cles de configuration propres a l'ancien emplacement, non recopiees.</summary>
        private static readonly string[] LocationKeys = ["install.path", "state.file"];

        /// <summary>
        /// Verifie au demarrage que le launcher est dans un dossier de Jedi Academy deja pris
        /// en charge. Sinon, propose l'installation propre. Faux si le demarrage normal ne
        /// doit pas continuer (assistant ouvert, ou jeu introuvable).
        /// </summary>
        private bool CheckGameFolder()
        {
            if (!_config.GetBool("install.check", true))
            {
                return true;
            }

            string? gameData = CurrentGameData();

            if (gameData is null)
            {
                Log($"No Jedi Academy files in {Path.GetFullPath(InstallRoot)}");
                ShowInstaller(
                    GameInstallService.FindJediAcademy(),
                    "The launcher is not in a Jedi Academy folder. Galactic Legacy will be installed "
                        + "in its own folder: the original Jedi Academy files are copied there, and "
                        + "your Jedi Academy installation is not modified.");
                return false;
            }

            if (File.Exists(StatePath))
            {
                return true;
            }

            using (var confirm = new TextPromptForm(
                _accentColor,
                DpiScale,
                "Install Galactic Legacy",
                "This Jedi Academy folder is not managed by the launcher yet.\n\n"
                    + "Recommended: install Galactic Legacy in its own folder. The original Jedi "
                    + "Academy files are copied there, and this folder stays untouched.\n\n"
                    + "Choose \"Use this folder\" to install the mod directly in "
                    + Path.GetFullPath(InstallRoot) + ".",
                "Install in a new folder",
                "Use this folder"))
            {
                if (confirm.ShowDialog(this) == DialogResult.OK)
                {
                    ShowInstaller(gameData, InstallerHint);
                    return false;
                }
            }

            // Dossier adopte tel quel : l'etat vide suffit a ne plus poser la question.
            try
            {
                ManifestService.SaveInstalledState(StatePath, new InstalledState());
                Log($"Using {Path.GetFullPath(InstallRoot)} as the game folder");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                LogError("installed.json", exception);
            }

            return true;
        }

        private const string InstallerHint =
            "Galactic Legacy will be installed in its own folder. The original Jedi Academy files "
            + "are copied there; your Jedi Academy installation is not modified.";

        /// <summary>GameData de Jedi Academy du dossier d'installation, ou null.</summary>
        private string? CurrentGameData()
        {
            return GameInstallService.ResolveGameData(Path.GetFullPath(InstallRoot));
        }

        private const string MoveHint =
            "Galactic Legacy will be moved to its own folder. The original Jedi Academy files are "
            + "copied there, then the mod files installed here are copied too and removed from this "
            + "folder. Your Jedi Academy files are not modified.";

        /// <summary>Chemins que l'installation ne deplace pas : le launcher, sa configuration, son etat.</summary>
        private string[] LauncherOwnFiles => [SelfUpdater.ExecutablePath, _config.FilePath, StatePath];

        /// <summary>
        /// Fichiers du mod deja installes ici, a deplacer avec l'installation ; vide s'il n'y
        /// a pas d'installation SWGL dans ce dossier.
        /// </summary>
        private List<(FileInfo Source, string Relative)> InstalledModFiles(out InstalledState state)
        {
            state = ManifestService.LoadInstalledState(StatePath);
            string? gameData = CurrentGameData();

            if (gameData is null || !File.Exists(StatePath) || state.Files.Count == 0)
            {
                return [];
            }

            return GameInstallService.ModFiles(
                gameData, Path.GetFullPath(InstallRoot), state, LauncherOwnFiles);
        }

        /// <summary>
        /// Assistant d'installation : dossier de Jedi Academy (pre-rempli s'il est connu) et
        /// dossier d'installation vide. La boite revient avec l'explication tant qu'un des
        /// deux ne convient pas. Si SWGL est deja installe ici, l'installation est deplacee.
        /// </summary>
        private void ShowInstaller(string? jediAcademy, string hint)
        {
            if (_isSyncing)
            {
                return;
            }

            List<(FileInfo Source, string Relative)> modFiles = InstalledModFiles(out _);
            bool move = modFiles.Count > 0;
            string source = jediAcademy ?? string.Empty;

            // Jedi Outcast est facultatif : propose s'il est trouve. Si ses assets sont deja
            // dans l'installation deplacee, ils partent avec elle et l'import est saute.
            string outcast = JediOutcastService.ResolveGameData(_config.GetString("jo.path", string.Empty))
                ?? GameInstallService.FindJediOutcast()
                ?? string.Empty;
            string destination = Path.Combine(
                Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\", "Games", "Star Wars Galactic Legacy");
            string message = move ? MoveHint : hint;

            if (source.Length == 0)
            {
                message += "\n\nJedi Academy was not found automatically: click Browse to select "
                    + "its folder (the one containing GameData, or GameData itself).";
            }

            while (true)
            {
                using var prompt = new TextPromptForm(
                    _accentColor,
                    DpiScale,
                    move ? "Move Galactic Legacy" : "Install Galactic Legacy",
                    message,
                    move ? "Move" : "Install",
                    "Cancel",
                    new PromptField("Jedi Academy folder", source, PromptBrowse.Folder),
                    new PromptField("Install folder (new or empty)", destination, PromptBrowse.Folder),
                    new PromptField(
                        "Jedi Outcast folder (optional: its assets are imported, leave empty to skip)",
                        outcast,
                        PromptBrowse.Folder,
                        Required: false));

                if (prompt.ShowDialog(this) != DialogResult.OK)
                {
                    if (CurrentGameData() is null)
                    {
                        SetStatus(
                            "Jedi Academy not found",
                            "Use Options > Install in a new folder... to install Galactic Legacy");
                    }

                    return;
                }

                source = prompt.ValueAt(0);
                destination = prompt.ValueAt(1);
                outcast = prompt.ValueAt(2);

                string? gameData = GameInstallService.ResolveGameData(source);
                string? outcastData = outcast.Length > 0 ? JediOutcastService.ResolveGameData(outcast) : null;
                long extraSize = (move ? InstalledModFiles(out _).Sum(file => file.Source.Length) : 0)
                    + (outcastData is null || modFiles.Any(file => file.Source.Name.StartsWith(
                        JediOutcastService.Prefix, StringComparison.OrdinalIgnoreCase))
                        ? 0
                        : JediOutcastService.AssetsSize(outcastData));

                string? error =
                    gameData is null
                        ? "Jedi Academy was not found in this folder: it must contain "
                            + "base\\assets0.pk3 to assets2.pk3."
                    : outcast.Length > 0 && outcastData is null
                        ? "Jedi Outcast was not found in this folder: it must contain jk2sp.exe, jk2mp.exe "
                            + "and jk2gamex86.dll. Leave the field empty to skip it."
                    : GameInstallService.ValidateDestination(destination, gameData, extraSize);

                if (error is null)
                {
                    _ = InstallAsync(gameData!, Path.GetFullPath(destination), move, outcastData);
                    return;
                }

                System.Media.SystemSounds.Hand.Play();
                message = error;
            }
        }

        /// <summary>
        /// Copie Jedi Academy, puis (deplacement) les fichiers du mod deja installes, puis le
        /// launcher dans le dossier d'installation. Y cree installed.json avec l'emplacement
        /// du launcher actuel, retire l'ancienne installation, et passe la main a la copie.
        /// Les assets de Jedi Outcast sont importes si son dossier est donne.
        /// </summary>
        private async Task InstallAsync(string gameData, string destination, bool move, string? outcast)
        {
            if (_isSyncing)
            {
                return;
            }

            // La musique peut etre lue depuis le dossier SWGL, qui sera supprime.
            if (move)
            {
                _music.Stop();
            }

            SetSyncing(true);
            _syncCancellation = new CancellationTokenSource();
            CancellationToken token = _syncCancellation.Token;
            IProgress<SyncProgress> progress = CreateProgress();
            var written = new List<string>();
            string? launcher = null;

            Log($"{(move ? "Move" : "Clean install")} — {gameData} -> {destination}"
                + (outcast is null ? string.Empty : $" (Jedi Outcast: {outcast})"));

            try
            {
                Directory.CreateDirectory(destination);

                await GameInstallService.CopyGameAsync(gameData, destination, written, progress, token);

                var state = new InstalledState { InstallerLocation = SelfUpdater.ExecutablePath };
                List<(FileInfo Source, string Relative)> modFiles = [];

                if (move)
                {
                    modFiles = InstalledModFiles(out InstalledState previous);

                    await GameInstallService.CopyFilesAsync(
                        modFiles, destination, "Moving Galactic Legacy", written, progress, token);

                    state = MovedState(previous, destination);
                }

                string targetBase = Path.Combine(destination, "base");

                if (outcast is not null && JediOutcastService.IsImported(outcast, targetBase))
                {
                    Log("Jedi Outcast assets already moved with the installation: import skipped");
                }
                else if (outcast is not null)
                {
                    written.AddRange(JediOutcastService.ImportedPaths(targetBase));
                    await JediOutcastService.ImportAssetsAsync(outcast, targetBase, progress, token);
                }

                progress.Report(new SyncProgress("Copying the launcher", destination, -1));
                launcher = await Task.Run(() => CopyLauncher(destination, written));

                string statePath = Path.Combine(destination, InstalledStateFileName);
                ManifestService.SaveInstalledState(statePath, state);
                written.Add(statePath);

                // Tout est en place dans le nouveau dossier : l'ancienne installation peut partir.
                if (move)
                {
                    await Task.Run(() => RemoveOldInstallation(modFiles, progress));
                }

                Process.Start(new ProcessStartInfo(launcher)
                {
                    UseShellExecute = false,
                    WorkingDirectory = destination,
                });

                Log($"Installed, starting {launcher}");
            }
            catch (OperationCanceledException)
            {
                GameInstallService.RemoveFiles(written, destination);
                progress.Report(new SyncProgress("Installation cancelled", string.Empty, 0));
                launcher = null;
            }
            catch (Exception exception)
            {
                LogError("install", exception);
                GameInstallService.RemoveFiles(written, destination);
                progress.Report(new SyncProgress("Installation failed", exception.Message, 0));
                launcher = null;
            }
            finally
            {
                _syncCancellation?.Dispose();
                _syncCancellation = null;
            }

            if (launcher is not null)
            {
                // La copie a pris le relais : elle supprimera ce launcher a son demarrage.
                Close();
                return;
            }

            SetSyncing(false);
        }

        /// <summary>
        /// Etat de l'installation deplacee : memes fichiers, chemins rendus relatifs au
        /// nouveau dossier. Les dates ayant ete conservees, rien n'est a recalculer.
        /// </summary>
        private InstalledState MovedState(InstalledState previous, string destination)
        {
            string root = Path.GetFullPath(InstallRoot);
            string gameData = CurrentGameData() ?? root;
            var files = new List<InstalledEntry>();

            foreach (InstalledEntry entry in previous.Files)
            {
                string relative;

                try
                {
                    relative = Path.GetRelativePath(gameData, ManifestService.ResolveLocalPath(root, entry.Path));
                }
                catch (InvalidDataException)
                {
                    continue;
                }

                if (File.Exists(Path.Combine(destination, relative)))
                {
                    files.Add(new InstalledEntry
                    {
                        Path = ManifestService.NormalizeRelativePath(relative),
                        Size = entry.Size,
                        ModifiedUtc = entry.ModifiedUtc,
                        Sha256 = entry.Sha256,
                    });
                }
            }

            var state = new InstalledState
            {
                Channel = previous.Channel,
                Version = previous.Version,
                BetaCode = previous.BetaCode,
                InstallerLocation = SelfUpdater.ExecutablePath,
            };

            state.Reset(files);
            return state;
        }

        /// <summary>
        /// Supprime de l'ancien dossier les fichiers du mod deplaces et son installed.json :
        /// il redevient un simple dossier de Jedi Academy.
        /// </summary>
        private void RemoveOldInstallation(
            List<(FileInfo Source, string Relative)> modFiles, IProgress<SyncProgress> progress)
        {
            for (int index = 0; index < modFiles.Count; index++)
            {
                FileInfo file = modFiles[index].Source;

                progress.Report(new SyncProgress(
                    $"Removing the old installation {index + 1}/{modFiles.Count}",
                    modFiles[index].Relative,
                    (index + 1) / (double)modFiles.Count));

                try
                {
                    File.SetAttributes(file.FullName, FileAttributes.Normal);
                    file.Delete();
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    Log($"Could not remove {file.FullName}: {exception.Message}");
                }
            }

            GameInstallService.RemoveEmptyFolders(
                Path.Combine(Path.GetFullPath(InstallRoot), UninstallService.ModFolder));

            try
            {
                File.Delete(StatePath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Log($"Could not remove {StatePath}: {exception.Message}");
            }
        }

        /// <summary>
        /// Copie l'executable, la musique posee a cote et la configuration (sans les cles
        /// liees a l'ancien emplacement). Retourne le chemin du launcher copie.
        /// </summary>
        private string CopyLauncher(string destination, List<string> written)
        {
            string self = SelfUpdater.ExecutablePath;
            string launcher = Path.Combine(destination, Path.GetFileName(self));

            File.Copy(self, launcher, overwrite: false);
            written.Add(launcher);

            string music = _config.GetString("music.file", "SWGL/launcher_music.mp3");

            if (music.Length > 0 && !Path.IsPathRooted(music))
            {
                string source = _config.ResolvePath(music);

                // Deja present si le dossier SWGL a ete deplace avec l'installation.
                if (File.Exists(source) && !File.Exists(Path.Combine(destination, music)))
                {
                    string target = Path.Combine(destination, music);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(source, target, overwrite: false);
                    written.Add(target);
                }
            }

            if (File.Exists(_config.FilePath))
            {
                string target = Path.Combine(destination, Path.GetFileName(_config.FilePath));

                IEnumerable<string> lines = File.ReadAllLines(_config.FilePath).Where(line =>
                {
                    string trimmed = line.Trim();
                    int separator = trimmed.IndexOfAny(['=', ':']);

                    return separator <= 0
                        || !LocationKeys.Contains(trimmed[..separator].Trim(), StringComparer.OrdinalIgnoreCase);
                });

                File.WriteAllLines(target, lines);
                written.Add(target);
            }

            return launcher;
        }

        /// <summary>
        /// Premier demarrage apres une installation propre : supprime le launcher qui l'a
        /// realisee. Par securite, seulement s'il est identique a celui-ci.
        /// </summary>
        private async Task RemovePreviousInstallerAsync()
        {
            InstalledState state = ManifestService.LoadInstalledState(StatePath);
            string? previous = state.InstallerLocation;

            if (string.IsNullOrEmpty(previous))
            {
                return;
            }

            // L'etat est remis a jour tout de suite : une synchronisation peut le reecrire.
            state.InstallerLocation = null;

            try
            {
                ManifestService.SaveInstalledState(StatePath, state);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                LogError("installed.json", exception);
            }

            string self = SelfUpdater.ExecutablePath;

            if (previous.Equals(self, StringComparison.OrdinalIgnoreCase)
                || !previous.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                || !File.Exists(previous))
            {
                return;
            }

            try
            {
                if (new FileInfo(previous).Length != new FileInfo(self).Length
                    || !string.Equals(
                        await ManifestService.ComputeSha256Async(previous, CancellationToken.None),
                        await ManifestService.ComputeSha256Async(self, CancellationToken.None),
                        StringComparison.OrdinalIgnoreCase))
                {
                    Log($"Previous launcher kept: {previous} differs from this one");
                    return;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Log($"Previous launcher kept: {exception.Message}");
                return;
            }

            // L'ancien launcher met un instant a se fermer apres avoir lance celui-ci.
            for (int attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    File.Delete(previous);
                    Log($"Previous launcher removed: {previous}");
                    RemovePreviousMusic(Path.GetDirectoryName(previous)!);
                    return;
                }
                catch (UnauthorizedAccessException)
                {
                    // Dossier protege (Program Files) : inutile d'insister.
                    break;
                }
                catch (IOException)
                {
                    await Task.Delay(500);
                }
            }

            Log($"Could not remove the previous launcher: {previous}");
        }

        /// <summary>
        /// Supprime la musique livree a cote de l'ancien launcher, puis son dossier (SWGL)
        /// s'il est reste vide : il peut contenir le mod si l'ancien dossier etait une installation.
        /// </summary>
        private void RemovePreviousMusic(string previousFolder)
        {
            string music = _config.GetString("music.file", "SWGL/launcher_music.mp3");

            if (music.Length == 0 || Path.IsPathRooted(music))
            {
                return;
            }

            string path = Path.GetFullPath(Path.Combine(previousFolder, music));

            // Jamais au-dela du dossier de l'ancien launcher.
            if (Path.GetRelativePath(previousFolder, path).StartsWith("..", StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    Log($"Previous launcher music removed: {path}");
                }

                string? folder = Path.GetDirectoryName(path);

                if (folder is not null
                    && !string.Equals(folder.TrimEnd('\\'), previousFolder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                    && Directory.Exists(folder)
                    && !Directory.EnumerateFileSystemEntries(folder).Any())
                {
                    Directory.Delete(folder);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Log($"Could not remove {path}: {exception.Message}");
            }
        }
    }
}
