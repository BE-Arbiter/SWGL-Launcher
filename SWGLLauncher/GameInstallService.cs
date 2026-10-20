using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SWGLLauncher
{
    /// <summary>
    /// Installation propre de SWGL : reconnaissance d'un dossier de Jedi Academy, recherche
    /// d'une installation existante, et copie des fichiers d'origine du jeu dans un dossier
    /// dedie au mod.
    /// </summary>
    internal static partial class GameInstallService
    {
        /// <summary>
        /// Fichiers d'origine du dossier GameData copies s'ils existent. Les executables et la
        /// dll du jeu (jasp.exe, jamp.exe, jagamex86.dll) ne le sont pas : le mod les remplace.
        /// </summary>
        private static readonly string[] RootFiles = ["OpenAL32.dll", "EaxMan.dll", "ifc22.dll"];

        /// <summary>Archives d'origine du dossier "base".</summary>
        private static readonly string[] BaseAssets = ["assets0.pk3", "assets1.pk3", "assets2.pk3"];

        /// <summary>
        /// Vrai si le dossier est un GameData de Jedi Academy, vierge ou deja mode : les trois
        /// archives de "base" doivent y etre. Les
        /// executables ne comptent pas, le mod les remplace par les siens.
        /// </summary>
        public static bool IsGameData(string folder)
        {
            return Directory.Exists(folder)
                && BaseAssets.All(asset => File.Exists(Path.Combine(folder, "base", asset)));
        }

        /// <summary>
        /// Retrouve le GameData a partir du dossier choisi : le dossier lui-meme, ou son
        /// sous-dossier "GameData" (racine du jeu). Null si aucun ne convient.
        /// </summary>
        public static string? ResolveGameData(string folder)
        {
            if (folder.Length == 0)
            {
                return null;
            }

            try
            {
                if (IsGameData(folder))
                {
                    return Path.GetFullPath(folder);
                }

                string nested = Path.Combine(folder, "GameData");
                return IsGameData(nested) ? Path.GetFullPath(nested) : null;
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                // Chemin mal forme saisi a la main.
                return null;
            }
        }

        /// <summary>
        /// Ou chercher un jeu : son nom tel que l'affichent Windows et GOG, sa cle LucasArts
        /// (version CD), son dossier dans les bibliotheques Steam et ses dossiers habituels.
        /// </summary>
        private sealed record GameSearch(
            string DisplayName,
            string LucasArtsKey,
            string SteamFolder,
            string[] KnownFolders,
            Func<string, string?> Resolve);

        private static readonly GameSearch JediAcademy = new(
            "Jedi Academy",
            @"LucasArts\Star Wars Jedi Knight Jedi Academy\1.0",
            "Jedi Academy",
            [
                @"%ProgramFiles(x86)%\LucasArts\Star Wars Jedi Knight Jedi Academy",
                @"%ProgramFiles%\LucasArts\Star Wars Jedi Knight Jedi Academy",
                @"%SystemDrive%\GOG Games\Star Wars Jedi Knight - Jedi Academy",
                @"%ProgramFiles(x86)%\GOG Galaxy\Games\Star Wars Jedi Knight - Jedi Academy",
            ],
            ResolveGameData);

        private static readonly GameSearch JediOutcast = new(
            "Jedi Outcast",
            @"LucasArts\Star Wars JK II Jedi Outcast\1.0",
            "Jedi Outcast",
            [
                @"%ProgramFiles(x86)%\LucasArts\Star Wars JK II Jedi Outcast",
                @"%ProgramFiles%\LucasArts\Star Wars JK II Jedi Outcast",
                @"%SystemDrive%\GOG Games\Star Wars Jedi Knight II - Jedi Outcast",
                @"%ProgramFiles(x86)%\GOG Galaxy\Games\Star Wars Jedi Knight II - Jedi Outcast",
            ],
            JediOutcastService.ResolveGameData);

        /// <summary>
        /// Cherche une installation de Jedi Academy, quelle que soit la version. Null si rien
        /// n'est trouve.
        /// </summary>
        public static string? FindJediAcademy() => Find(JediAcademy);

        /// <summary>Cherche une installation de Jedi Outcast (pour en importer les assets).</summary>
        public static string? FindJediOutcast() => Find(JediOutcast);

        /// <summary>
        /// Registre (CD, GOG, programmes installes), bibliotheques Steam, puis emplacements
        /// habituels : le premier dossier valide l'emporte.
        /// </summary>
        private static string? Find(GameSearch game)
        {
            IEnumerable<string> candidates = RegistryFolders(game)
                .Concat(SteamLibraries().Select(library => Path.Combine(library, "steamapps", "common", game.SteamFolder)))
                .Concat(game.KnownFolders.Select(Environment.ExpandEnvironmentVariables));

            foreach (string folder in candidates)
            {
                string? found = game.Resolve(folder);

                if (found is not null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>
        /// Dossiers d'installation declares dans le registre : cle LucasArts de la version CD,
        /// jeux GOG, et liste des programmes installes (toutes versions, Steam compris).
        /// </summary>
        private static List<string> RegistryFolders(GameSearch game)
        {
            var folders = new List<string>();

            bool Matches(string? name) =>
                name is not null && name.Contains(game.DisplayName, StringComparison.OrdinalIgnoreCase);

            foreach (string software in new[] { @"SOFTWARE\WOW6432Node", @"SOFTWARE" })
            {
                // Version CD (LucasArts).
                using (RegistryKey? lucasArts = Registry.LocalMachine.OpenSubKey(software + @"\" + game.LucasArtsKey))
                {
                    AddValue(folders, lucasArts, "Install Path");
                }

                // Version GOG : une sous-cle par jeu, avec son nom et son dossier.
                using (RegistryKey? gog = Registry.LocalMachine.OpenSubKey(software + @"\GOG.com\Games"))
                {
                    foreach (string id in gog?.GetSubKeyNames() ?? [])
                    {
                        using RegistryKey? entry = gog!.OpenSubKey(id);

                        if (Matches(entry?.GetValue("gameName") as string))
                        {
                            AddValue(folders, entry, "path");
                        }
                    }
                }

                // Programmes installes, tous editeurs confondus.
                foreach (RegistryKey root in new[] { Registry.LocalMachine, Registry.CurrentUser })
                {
                    using RegistryKey? uninstall = root.OpenSubKey(
                        software + @"\Microsoft\Windows\CurrentVersion\Uninstall");

                    foreach (string id in uninstall?.GetSubKeyNames() ?? [])
                    {
                        using RegistryKey? program = uninstall!.OpenSubKey(id);

                        if (Matches(program?.GetValue("DisplayName") as string))
                        {
                            AddValue(folders, program, "InstallLocation");
                        }
                    }
                }
            }

            return [.. folders.Distinct(StringComparer.OrdinalIgnoreCase)];
        }

        private static void AddValue(List<string> folders, RegistryKey? key, string name)
        {
            if (key?.GetValue(name) is string value && value.Trim().Length > 0)
            {
                folders.Add(value.Trim().Trim('"'));
            }
        }

        /// <summary>Dossiers des bibliotheques Steam : celle de Steam, puis celles de libraryfolders.vdf.</summary>
        private static IEnumerable<string> SteamLibraries()
        {
            string? steamRoot = SteamShortcutsService.ResolveSteamRoot(null);

            if (steamRoot is null)
            {
                yield break;
            }

            yield return steamRoot;

            string libraries = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            string content;

            try
            {
                content = File.Exists(libraries) ? File.ReadAllText(libraries) : string.Empty;
            }
            catch (IOException)
            {
                yield break;
            }

            // Lignes du type : "path"		"D:\\SteamLibrary"
            foreach (Match match in LibraryPathPattern().Matches(content))
            {
                yield return match.Groups[1].Value.Replace(@"\\", @"\");
            }
        }

        [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)]
        private static partial Regex LibraryPathPattern();

        /// <summary>
        /// Raison pour laquelle le dossier ne peut pas recevoir l'installation, ou null s'il
        /// convient : il doit etre vide (ou ne pas exister), hors de Program Files, et ne pas
        /// se trouver dans le dossier du jeu copie.
        /// </summary>
        public static string? ValidateDestination(string destination, string sourceGameData, long extraBytes = 0)
        {
            string full;

            try
            {
                full = Path.GetFullPath(destination);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return "The install folder is not a valid path.";
            }

            if (Path.GetPathRoot(full) == full)
            {
                return "Choose a folder, not the root of a drive.";
            }

            foreach (Environment.SpecialFolder special in new[]
            {
                Environment.SpecialFolder.ProgramFiles,
                Environment.SpecialFolder.ProgramFilesX86,
            })
            {
                string protectedFolder = Environment.GetFolderPath(special);

                if (protectedFolder.Length > 0 && IsSameOrInside(full, protectedFolder))
                {
                    return "Choose a folder outside Program Files: Windows protects it, and the "
                        + "launcher could not update the game there.";
                }
            }

            // Un dossier "GameData" appartient a la racine du jeu : on l'exclut aussi.
            string source = Path.GetFullPath(sourceGameData).TrimEnd('\\');
            string sourceRoot = Path.GetFileName(source).Equals("GameData", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(source) ?? source
                : source;

            if (IsSameOrInside(full, sourceRoot))
            {
                return "The install folder cannot be inside the Jedi Academy folder.";
            }

            if (Directory.Exists(full) && Directory.EnumerateFileSystemEntries(full).Any())
            {
                return "The install folder must be empty.";
            }

            if (File.Exists(full))
            {
                return "The install folder is a file.";
            }

            long required = GameSize(sourceGameData) + extraBytes;

            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(full)!);

                if (drive.IsReady && drive.AvailableFreeSpace < required + (64L * 1024 * 1024))
                {
                    return $"Not enough free space on {drive.Name}: "
                        + $"{required / (1024 * 1024)} MB are needed.";
                }
            }
            catch (Exception exception) when (exception is ArgumentException or IOException)
            {
                // Lecteur reseau ou inconnu : la copie dira elle-meme si la place manque.
            }

            return null;
        }

        private static bool IsSameOrInside(string path, string folder)
        {
            string relative = Path.GetRelativePath(Path.GetFullPath(folder), path);

            return relative == "."
                || !(relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative));
        }

        /// <summary>Fichiers d'origine a copier, avec leur chemin relatif dans GameData.</summary>
        private static List<(FileInfo Source, string Relative)> SourceFiles(string sourceGameData)
        {
            var files = new List<(FileInfo, string)>();

            foreach (string name in RootFiles)
            {
                var file = new FileInfo(Path.Combine(sourceGameData, name));

                if (file.Exists)
                {
                    files.Add((file, file.Name));
                }
            }

            foreach (string asset in BaseAssets)
            {
                var file = new FileInfo(Path.Combine(sourceGameData, "base", asset));

                if (file.Exists)
                {
                    files.Add((file, Path.Combine("base", file.Name)));
                }
            }

            return files;
        }

        /// <summary>
        /// Fichiers d'une installation SWGL existante, a deplacer avec elle : le dossier SWGL
        /// en entier, le pk3 du mod et les assets de Jedi Outcast importes dans "base", et
        /// tout fichier suivi dans l'etat local. Les chemins sont relatifs au GameData.
        /// </summary>
        /// <param name="exclude">Chemins absolus a laisser : le launcher, sa configuration, son etat.</param>
        public static List<(FileInfo Source, string Relative)> ModFiles(
            string gameData, string installRoot, InstalledState state, IReadOnlyCollection<string> exclude)
        {
            var paths = new List<string>();
            string modBase = JediOutcastService.ResolveTargetBase(installRoot);

            paths.Add(Path.Combine(modBase, UninstallService.ModPk3));

            if (Directory.Exists(modBase))
            {
                paths.AddRange(Directory.EnumerateFiles(modBase, JediOutcastService.Prefix + "*"));
            }

            string modFolder = Path.Combine(installRoot, UninstallService.ModFolder);

            if (Directory.Exists(modFolder))
            {
                paths.AddRange(Directory.EnumerateFiles(modFolder, "*", SearchOption.AllDirectories));
            }

            foreach (InstalledEntry entry in state.Files)
            {
                try
                {
                    paths.Add(ManifestService.ResolveLocalPath(installRoot, entry.Path));
                }
                catch (InvalidDataException)
                {
                    // Chemin hors du dossier d'installation : jamais touche.
                }
            }

            var excluded = new HashSet<string>(exclude.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
            var files = new List<(FileInfo, string)>();

            foreach (string path in paths
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(path => !excluded.Contains(path)))
            {
                var file = new FileInfo(path);
                string relative = Path.GetRelativePath(gameData, path);

                if (file.Exists && !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative))
                {
                    files.Add((file, relative));
                }
            }

            return files;
        }

        /// <summary>Taille des fichiers d'origine de Jedi Academy a copier.</summary>
        public static long GameSize(string sourceGameData) =>
            SourceFiles(sourceGameData).Sum(file => file.Source.Length);

        /// <summary>
        /// Copie les fichiers d'origine de Jedi Academy (archives de base et bibliotheques
        /// audio, sans executables du jeu, configuration, sauvegarde ni mod) vers le dossier
        /// d'installation. Les executables viennent ensuite du mod, par la mise a jour.
        /// </summary>
        public static Task CopyGameAsync(
            string sourceGameData,
            string destination,
            List<string> written,
            IProgress<SyncProgress>? progress,
            CancellationToken cancellationToken)
        {
            return CopyFilesAsync(
                SourceFiles(sourceGameData), destination, "Copying Jedi Academy", written, progress, cancellationToken);
        }

        /// <summary>
        /// Copie des fichiers vers le dossier d'installation, en gardant leur date de
        /// modification (l'etat local reste valable sans tout recalculer). Chaque fichier
        /// ecrit est ajoute a <paramref name="written"/>, pour pouvoir annuler.
        /// </summary>
        public static async Task CopyFilesAsync(
            List<(FileInfo Source, string Relative)> files,
            string destination,
            string label,
            List<string> written,
            IProgress<SyncProgress>? progress,
            CancellationToken cancellationToken)
        {
            long totalBytes = files.Sum(file => file.Source.Length);
            long copiedBytes = 0;

            for (int index = 0; index < files.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                (FileInfo source, string relative) = files[index];
                string target = Path.Combine(destination, relative);
                string temporary = target + ".part";
                long fileStart = copiedBytes;
                int number = index + 1;

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                written.Add(temporary);

                await CopyWithProgressAsync(
                    source.FullName,
                    temporary,
                    read => progress?.Report(new SyncProgress(
                        $"{label} {number}/{files.Count}",
                        relative,
                        totalBytes > 0 ? (fileStart + read) / (double)totalBytes : -1)),
                    cancellationToken);

                File.SetLastWriteTimeUtc(temporary, source.LastWriteTimeUtc);
                File.Move(temporary, target, overwrite: true);
                written[^1] = target;
                copiedBytes = fileStart + source.Length;
            }
        }

        /// <summary>
        /// Annule une installation : retire les fichiers ecrits, puis les dossiers restes
        /// vides sous le dossier d'installation (et lui-meme s'il est vide).
        /// </summary>
        public static void RemoveFiles(IEnumerable<string> files, string destination)
        {
            foreach (string file in files)
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Tant pis : le dossier restera non vide, l'utilisateur pourra le vider.
                }
            }

            RemoveEmptyFolders(destination);
        }

        public static void RemoveEmptyFolders(string folder)
        {
            if (!Directory.Exists(folder))
            {
                return;
            }

            foreach (string child in Directory.EnumerateDirectories(folder))
            {
                RemoveEmptyFolders(child);
            }

            try
            {
                if (!Directory.EnumerateFileSystemEntries(folder).Any())
                {
                    Directory.Delete(folder);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        private static async Task CopyWithProgressAsync(
            string sourcePath,
            string destinationPath,
            Action<long> onProgress,
            CancellationToken cancellationToken)
        {
            const int bufferSize = 1024 * 1024;

            await using FileStream source = new(
                sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true);

            await using FileStream destination = new(
                destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, useAsync: true);

            byte[] buffer = new byte[bufferSize];
            long total = 0;
            int read;

            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                total += read;
                onProgress(total);
            }
        }
    }
}
