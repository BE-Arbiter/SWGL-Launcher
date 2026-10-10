namespace SWGLLauncher
{
    /// <summary>
    /// Import des assets de Jedi Outcast : on valide le dossier choisi, puis on copie
    /// Assets0/1/2.pk3 dans le "base" de SWGL en les prefixant, pour qu'ils se chargent
    /// avant le reste.
    /// </summary>
    internal static class JediOutcastService
    {
        /// <summary>Prefixe ajoute aux pk3 importes.</summary>
        public const string Prefix = "0_JKO_";

        /// <summary>Fichiers qui identifient un GameData de Jedi Outcast.</summary>
        private static readonly string[] Markers = ["jk2sp.exe", "jk2mp.exe", "jk2gamex86.dll"];

        /// <summary>Assets copies vers SWGL.</summary>
        private static readonly string[] Assets = ["Assets0.pk3", "Assets1.pk3", "Assets2.pk3"];

        /// <summary>
        /// Retrouve le dossier "GameData" de Jedi Outcast a partir du dossier choisi par
        /// l'utilisateur : soit &lt;dossier&gt;/GameData, soit le dossier lui-meme.
        /// Retourne null si aucun des deux ne contient les fichiers attendus.
        /// </summary>
        public static string? ResolveGameData(string selectedFolder)
        {
            if (selectedFolder.Length == 0)
            {
                return null;
            }

            string candidate = Path.Combine(selectedFolder, "GameData");

            if (ContainsMarkers(candidate))
            {
                return Path.GetFullPath(candidate);
            }

            return ContainsMarkers(selectedFolder) ? Path.GetFullPath(selectedFolder) : null;
        }

        private static bool ContainsMarkers(string folder)
        {
            return Directory.Exists(folder)
                && Markers.All(marker => File.Exists(Path.Combine(folder, marker)));
        }

        /// <summary>
        /// Dossier "base" de SWGL ou deposer les assets : &lt;install&gt;/base, ou
        /// &lt;install&gt;/GameData/base si le launcher est place au-dessus de GameData.
        /// </summary>
        public static string ResolveTargetBase(string installRoot)
        {
            string direct = Path.Combine(installRoot, "base");
            if (Directory.Exists(direct))
            {
                return direct;
            }

            string nested = Path.Combine(installRoot, "GameData", "base");
            return Directory.Exists(nested) ? nested : direct;
        }

        /// <summary>Taille des assets a importer (ceux qui existent).</summary>
        public static long AssetsSize(string jediOutcastGameData) => Assets
            .Select(asset => new FileInfo(Path.Combine(jediOutcastGameData, "base", asset)))
            .Where(file => file.Exists)
            .Sum(file => file.Length);

        /// <summary>
        /// Vrai si les assets sont deja dans "base", a la meme taille que ceux de Jedi Outcast :
        /// un nouvel import ne changerait rien.
        /// </summary>
        public static bool IsImported(string jediOutcastGameData, string targetBase) => Assets.All(asset =>
        {
            var source = new FileInfo(Path.Combine(jediOutcastGameData, "base", asset));
            var target = new FileInfo(Path.Combine(targetBase, Prefix + asset));
            return source.Exists && target.Exists && source.Length == target.Length;
        });

        /// <summary>Fichiers que l'import ecrit dans "base", temporaires compris : pour l'annuler.</summary>
        public static IEnumerable<string> ImportedPaths(string targetBase) => Assets
            .Select(asset => Path.Combine(targetBase, Prefix + asset))
            .SelectMany(path => new[] { path, path + ".part" });

        /// <summary>
        /// Copie les assets de Jedi Outcast vers le dossier "base" de SWGL.
        /// </summary>
        /// <returns>Le nombre de fichiers copies.</returns>
        public static async Task<int> ImportAssetsAsync(
            string jediOutcastGameData,
            string targetBase,
            IProgress<SyncProgress>? progress,
            CancellationToken cancellationToken)
        {
            string sourceBase = Path.Combine(jediOutcastGameData, "base");

            if (!Directory.Exists(sourceBase))
            {
                throw new DirectoryNotFoundException(
                    $"No \"base\" folder in {jediOutcastGameData}.");
            }

            var sources = new List<FileInfo>();

            foreach (string asset in Assets)
            {
                var file = new FileInfo(Path.Combine(sourceBase, asset));

                if (!file.Exists)
                {
                    throw new FileNotFoundException($"{asset} is missing from {sourceBase}.");
                }

                sources.Add(file);
            }

            Directory.CreateDirectory(targetBase);

            long totalBytes = sources.Sum(file => file.Length);
            long copiedBytes = 0;
            int copied = 0;

            foreach (FileInfo source in sources)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string destination = Path.Combine(targetBase, Prefix + source.Name);
                string temporary = destination + ".part";
                long fileStart = copiedBytes;

                await CopyWithProgressAsync(
                    source.FullName,
                    temporary,
                    read => progress?.Report(new SyncProgress(
                        $"Importing Jedi Outcast assets {copied + 1}/{sources.Count}",
                        $"{Prefix}{source.Name}",
                        totalBytes > 0 ? (fileStart + read) / (double)totalBytes : -1)),
                    cancellationToken);

                File.Move(temporary, destination, overwrite: true);

                copiedBytes = fileStart + source.Length;
                copied++;
            }

            return copied;
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
