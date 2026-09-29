namespace SWGLLauncher
{
    /// <summary>
    /// Desinstallation du mod : le pk3 SWGL et les assets de Jedi Outcast importes dans
    /// "base", le dossier SWGL, et les executables et dll poses par le launcher. Le reste
    /// du GameData, le launcher, sa configuration et son etat restent en place.
    /// </summary>
    internal static class UninstallService
    {
        /// <summary>Dossier du mod, supprime en entier.</summary>
        private const string ModFolder = "SWGL";

        /// <summary>Pk3 du mod depose dans "base".</summary>
        private const string ModPk3 = "zzzzzzz_SWGL_JKJO.pk3";

        /// <summary>Resultat : fichiers supprimes, et ceux qui n'ont pas pu l'etre.</summary>
        public sealed record Result(int Removed, IReadOnlyList<string> Failed);

        /// <param name="installRoot">Dossier d'installation (le GameData).</param>
        /// <param name="statePath">Etat local : les executables et dll y sont listes.</param>
        /// <param name="keep">Chemins absolus a epargner : le launcher, sa configuration, son etat.</param>
        public static Result Uninstall(
            string installRoot,
            string statePath,
            IReadOnlyCollection<string> keep,
            IProgress<SyncProgress>? progress)
        {
            string root = Path.GetFullPath(installRoot);
            var kept = new HashSet<string>(keep.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
            InstalledState state = ManifestService.LoadInstalledState(statePath);

            var targets = new List<string>();
            string modBase = JediOutcastService.ResolveTargetBase(root);

            targets.Add(Path.Combine(modBase, ModPk3));

            if (Directory.Exists(modBase))
            {
                targets.AddRange(Directory.EnumerateFiles(modBase, JediOutcastService.Prefix + "*"));
            }

            string modFolder = Path.Combine(root, ModFolder);

            if (Directory.Exists(modFolder))
            {
                targets.AddRange(Directory.EnumerateFiles(modFolder, "*", SearchOption.AllDirectories));
            }

            foreach (InstalledEntry entry in state.Files)
            {
                string extension = Path.GetExtension(entry.Path);

                if (!extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
                    && !extension.Equals(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    targets.Add(ManifestService.ResolveLocalPath(root, entry.Path));
                }
                catch (InvalidDataException)
                {
                    // Chemin hors du dossier d'installation : jamais touche.
                }
            }

            List<string> files = [.. targets
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(path => !kept.Contains(path) && File.Exists(path))];

            int removed = 0;
            var failed = new List<string>();

            for (int index = 0; index < files.Count; index++)
            {
                string path = files[index];
                string relative = Path.GetRelativePath(root, path);

                progress?.Report(new SyncProgress(
                    $"Uninstalling {index + 1}/{files.Count}", relative, (index + 1) / (double)files.Count));

                try
                {
                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                    removed++;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    failed.Add($"{relative}: {exception.Message}");
                }
            }

            RemoveEmptyFolders(modFolder);
            ForgetMissingFiles(root, statePath, state);

            return new Result(removed, failed);
        }

        /// <summary>Supprime les dossiers vides, du plus profond au plus haut.</summary>
        private static void RemoveEmptyFolders(string folder)
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
                // Un dossier ouvert ailleurs reste : sans consequence.
            }
        }

        /// <summary>
        /// Retire de l'etat local les fichiers qui n'existent plus : la prochaine mise a
        /// jour repart d'une installation vide. Le code de beta est conserve.
        /// </summary>
        private static void ForgetMissingFiles(string root, string statePath, InstalledState state)
        {
            if (!File.Exists(statePath))
            {
                return;
            }

            List<InstalledEntry> remaining = [.. state.Files.Where(entry =>
            {
                try
                {
                    return File.Exists(ManifestService.ResolveLocalPath(root, entry.Path));
                }
                catch (InvalidDataException)
                {
                    return false;
                }
            })];

            if (remaining.Count == 0)
            {
                state.Channel = string.Empty;
                state.Version = string.Empty;
            }

            state.Reset(remaining);
            ManifestService.SaveInstalledState(statePath, state);
        }
    }
}
