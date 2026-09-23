using FluentFTP;

namespace SWGLLauncher
{
    /// <summary>Travail a effectuer pour amener l'installation a l'etat du manifeste.</summary>
    internal sealed class SyncPlan
    {
        public List<ManifestEntry> Download { get; } = [];

        /// <summary>Chemins relatifs a supprimer (fichiers geres par le launcher et disparus du manifeste).</summary>
        public List<string> Delete { get; } = [];

        /// <summary>Fichiers deja corrects, conserves tels quels dans l'etat local.</summary>
        public List<InstalledEntry> Keep { get; } = [];

        public long TotalBytes => Download.Sum(entry => entry.Size);

        public bool IsUpToDate => Download.Count == 0 && Delete.Count == 0;
    }

    /// <summary>
    /// Compare l'installation locale au manifeste, puis applique les differences.
    /// L'operation est idempotente : relancer une synchronisation interrompue reprend
    /// simplement ce qui manque.
    /// </summary>
    internal sealed class SyncEngine
    {
        private const string PartialSuffix = ".part";

        private readonly string _installRoot;
        private readonly string _statePath;

        public SyncEngine(string installRoot, string statePath)
        {
            _installRoot = installRoot;
            _statePath = statePath;
        }

        /// <summary>Code de beta a memoriser dans l'etat local (vide pour le canal public).</summary>
        public string BetaCode { get; init; } = string.Empty;

        /// <summary>
        /// Chemins relatifs jamais supprimes, quoi qu'il arrive : l'executable du launcher,
        /// sa configuration, son etat, ses propres visuels.
        /// </summary>
        public IReadOnlyCollection<string> Protected { get; init; } = [];

        /// <summary>
        /// Seuls fichiers que le nettoyage peut supprimer, quand ils ne figurent pas au
        /// manifeste : tout le reste du dossier d'installation est laisse en place.
        /// "*" ne traverse pas les dossiers, "**" si.
        /// </summary>
        public IReadOnlyCollection<string> DeletablePatterns { get; init; } = [];

        /// <summary>Journal des operations effectuees (suppressions, telechargements...).</summary>
        public Action<string>? Log { get; init; }

        /// <summary>
        /// Nombre de nouvelles tentatives pour un telechargement interrompu par le reseau.
        /// Chacune se reconnecte et reprend la ou le fichier partiel s'est arrete.
        /// </summary>
        public int DownloadRetries { get; init; } = 3;

        /// <summary>
        /// Determine ce qui doit etre telecharge et supprime.
        /// </summary>
        /// <param name="fullVerify">
        /// Vrai pour recalculer l'empreinte de chaque fichier (verification complete) ;
        /// faux pour se fier a la taille et a la date memorisees dans l'etat local.
        /// </param>
        public async Task<SyncPlan> BuildPlanAsync(
            Manifest manifest,
            bool fullVerify,
            IProgress<SyncProgress>? progress,
            CancellationToken cancellationToken)
        {
            InstalledState state = ManifestService.LoadInstalledState(_statePath);
            var plan = new SyncPlan();
            var manifestPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < manifest.Files.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                ManifestEntry entry = manifest.Files[i];
                manifestPaths.Add(entry.Path);

                progress?.Report(new SyncProgress(
                    $"Checking {i + 1}/{manifest.Files.Count}",
                    entry.Path,
                    (i + 1) / (double)Math.Max(1, manifest.Files.Count)));

                string localPath = ManifestService.ResolveLocalPath(_installRoot, entry.Path);
                var file = new FileInfo(localPath);

                if (!file.Exists || file.Length != entry.Size)
                {
                    plan.Download.Add(entry);
                    continue;
                }

                // Sans empreinte dans le manifeste, la taille fait foi.
                if (entry.Sha256.Length == 0)
                {
                    plan.Keep.Add(CreateEntry(entry.Path, file, string.Empty));
                    continue;
                }

                InstalledEntry? known = state.Find(entry.Path);
                bool trustState = !fullVerify
                    && known is not null
                    && known.Size == file.Length
                    && known.ModifiedUtc == file.LastWriteTimeUtc
                    && known.Sha256.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase);

                if (trustState)
                {
                    plan.Keep.Add(known!);
                    continue;
                }

                string hash = await ManifestService.ComputeSha256Async(localPath, cancellationToken);

                if (hash.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    plan.Keep.Add(CreateEntry(entry.Path, file, hash));
                }
                else
                {
                    plan.Download.Add(entry);
                }
            }

            // Nettoyage des fichiers absents du manifeste : ceux que le launcher a lui-meme
            // installes pour un canal (les fichiers d'une beta quand on revient au public),
            // ceux couverts par les motifs supprimables, et ceux que le manifeste designe.
            progress?.Report(new SyncProgress("Looking for obsolete files", _installRoot));

            plan.Delete.AddRange(FindObsoleteFiles(manifestPaths, TrackedPaths(state), manifest.Delete, cancellationToken)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase));

            return plan;
        }

        /// <summary>Fichiers installes par le launcher lors des mises a jour precedentes.</summary>
        private static HashSet<string> TrackedPaths(InstalledState state) =>
            new(state.Files.Select(file => file.Path), StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Parcourt le dossier d'installation et retient les fichiers supprimables qui
        /// n'appartiennent pas au manifeste.
        /// </summary>
        private IEnumerable<string> FindObsoleteFiles(
            HashSet<string> manifestPaths,
            HashSet<string> trackedPaths,
            IReadOnlyCollection<string> forcedPatterns,
            CancellationToken cancellationToken)
        {
            if (!Directory.Exists(_installRoot))
            {
                yield break;
            }

            foreach (string file in Directory.EnumerateFiles(
                _installRoot, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();

                string relative = ManifestService.NormalizeRelativePath(
                    Path.GetRelativePath(_installRoot, file));

                if (relative.Length > 0
                    && !manifestPaths.Contains(relative)
                    && (trackedPaths.Contains(relative) || IsDeletable(relative, forcedPatterns))
                    && !Protected.Contains(relative, StringComparer.OrdinalIgnoreCase))
                {
                    yield return relative;
                }
            }
        }

        /// <summary>
        /// Vrai si le chemin est couvert par un motif supprimable, ou par un motif que le
        /// manifeste impose, et n'est pas protege.
        /// </summary>
        private bool IsDeletable(string relativePath, IReadOnlyCollection<string> forcedPatterns)
        {
            if (Protected.Contains(relativePath, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            return DeletablePatterns.Concat(forcedPatterns)
                .Any(pattern => ManifestService.MatchesPattern(relativePath, pattern));
        }

        /// <summary>
        /// Applique le plan : suppressions, puis telechargements verifies par empreinte.
        /// </summary>
        public async Task ApplyAsync(
            SyncPlan plan,
            Manifest manifest,
            FtpClientService ftp,
            IProgress<SyncProgress>? progress,
            CancellationToken cancellationToken)
        {
            var installed = new List<InstalledEntry>(plan.Keep);

            // Les suppressions d'abord : ces fichiers ne font plus partie du canal, et
            // liberer la place avant de telecharger evite de saturer le disque.
            foreach (string path in plan.Delete)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new SyncProgress("Removing", path));

                if (DeleteLocalFile(path))
                {
                    Log?.Invoke($"Removed {path}");
                }
                else
                {
                    // Garde en memoire : la suppression sera retentee a la prochaine mise a jour.
                    installed.Add(new InstalledEntry { Path = path });
                }
            }

            SaveState(manifest, installed);

            long totalBytes = plan.TotalBytes;
            long completedBytes = 0;

            for (int i = 0; i < plan.Download.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                ManifestEntry entry = plan.Download[i];
                string localPath = ManifestService.ResolveLocalPath(_installRoot, entry.Path);
                string header = $"Downloading {i + 1}/{plan.Download.Count}";
                long fileStartBytes = completedBytes;

                Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);

                var fileProgress = new Progress<FtpProgress>(report =>
                {
                    double fraction = totalBytes > 0
                        ? (fileStartBytes + report.TransferredBytes) / (double)totalBytes
                        : -1;

                    progress?.Report(new SyncProgress(
                        header,
                        $"{entry.Path}  —  {FormatSpeed(report.TransferSpeed)}",
                        Math.Clamp(fraction, 0, 1)));
                });

                progress?.Report(new SyncProgress(header, entry.Path,
                    totalBytes > 0 ? fileStartBytes / (double)totalBytes : -1));

                bool resuming = File.Exists(localPath + PartialSuffix);
                InstalledEntry result;

                for (int attempt = 1; ; attempt++)
                {
                    try
                    {
                        result = await DownloadVerifiedAsync(
                            ftp, entry, localPath, fileProgress, cancellationToken);
                        break;
                    }
                    catch (Exception exception) when (
                        attempt <= DownloadRetries
                        && !cancellationToken.IsCancellationRequested
                        && IsTransient(exception))
                    {
                        // Coupure reseau : le fichier partiel est garde, on se reconnecte
                        // et le telechargement reprend a l'octet ou il s'est arrete.
                        Log?.Invoke($"Download of {entry.Path} interrupted ({Innermost(exception).Message}), "
                            + $"retrying {attempt}/{DownloadRetries}");

                        progress?.Report(new SyncProgress(
                            $"Reconnecting ({attempt}/{DownloadRetries})",
                            entry.Path,
                            totalBytes > 0 ? fileStartBytes / (double)totalBytes : -1));

                        await Task.Delay(TimeSpan.FromSeconds(2 * attempt), cancellationToken);
                        await ftp.ReconnectAsync(cancellationToken);
                        resuming = true;
                    }
                }

                Log?.Invoke(resuming
                    ? $"Downloaded {entry.Path} ({FormatSize(entry.Size)}, resumed)"
                    : $"Downloaded {entry.Path} ({FormatSize(entry.Size)})");

                installed.RemoveAll(
                    existing => existing.Path.Equals(entry.Path, StringComparison.OrdinalIgnoreCase));
                installed.Add(result);

                completedBytes = fileStartBytes + entry.Size;

                // L'etat est ecrit apres chaque fichier : une coupure ne fait pas perdre
                // le travail deja valide.
                SaveState(manifest, installed);
            }

            SaveState(manifest, installed);
        }

        /// <summary>
        /// Telecharge dans un fichier temporaire, controle l'empreinte, puis met en place.
        /// Un fichier dont l'empreinte est fausse est retelecharge une fois depuis zero
        /// (cas classique d'une reprise sur un fichier modifie entre-temps sur le serveur).
        /// </summary>
        private async Task<InstalledEntry> DownloadVerifiedAsync(
            FtpClientService ftp,
            ManifestEntry entry,
            string localPath,
            IProgress<FtpProgress> progress,
            CancellationToken cancellationToken)
        {
            string tempPath = localPath + PartialSuffix;

            for (int attempt = 0; attempt < 2; attempt++)
            {
                await ftp.DownloadFileAsync(entry.RemotePath, tempPath, progress, cancellationToken);

                string hash = entry.Sha256.Length > 0
                    ? await ManifestService.ComputeSha256Async(tempPath, cancellationToken)
                    : string.Empty;

                bool valid = entry.Sha256.Length == 0
                    ? new FileInfo(tempPath).Length == entry.Size
                    : hash.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase);

                if (valid)
                {
                    File.Move(tempPath, localPath, overwrite: true);
                    var file = new FileInfo(localPath);
                    return CreateEntry(entry.Path, file, hash);
                }

                // Reprise probablement incoherente : on repart du debut.
                Log?.Invoke(attempt == 0
                    ? $"Checksum mismatch on {entry.Path}, downloading it again from scratch"
                    : $"Checksum mismatch on {entry.Path} again, giving up");
                File.Delete(tempPath);
            }

            // Pas une IOException : ce n'est pas une coupure a retenter, le fichier recu est faux.
            throw new InvalidDataException(
                $"{entry.Path} failed its checksum after download.");
        }

        /// <summary>
        /// Vrai pour une panne reseau passagere (timeout, connexion coupee), qui vaut la peine
        /// d'etre retentee. Un refus d'authentification ou une annulation ne le sont pas.
        /// </summary>
        private static bool IsTransient(Exception exception)
        {
            bool transient = false;

            for (Exception? current = exception; current is not null; current = current.InnerException)
            {
                switch (current)
                {
                    case OperationCanceledException:
                    case FluentFTP.Exceptions.FtpAuthenticationException:
                    case InvalidDataException:
                        return false;

                    case TimeoutException:
                    case IOException:
                    case System.Net.Sockets.SocketException:
                        transient = true;
                        break;

                    // FluentFTP signale ainsi une connexion deja tombee quand le fichier
                    // suivant demarre ("No connection to the server exists") : la
                    // reconnexion la retablit.
                    case InvalidOperationException:
                        transient = true;
                        break;
                }
            }

            return transient;
        }

        private static Exception Innermost(Exception exception)
        {
            Exception current = exception;

            while (current.InnerException is not null)
            {
                current = current.InnerException;
            }

            return current;
        }

        /// <returns>Vrai si la suppression a abouti (ou s'il n'y avait rien a supprimer).</returns>
        private bool DeleteLocalFile(string relativePath)
        {
            try
            {
                string localPath = ManifestService.ResolveLocalPath(_installRoot, relativePath);

                if (File.Exists(localPath))
                {
                    File.Delete(localPath);
                }

                string partial = localPath + PartialSuffix;
                if (File.Exists(partial))
                {
                    File.Delete(partial);
                }

                return true;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                // Un fichier verrouille ou refuse ne doit pas interrompre la mise a jour :
                // il sera retente au prochain passage.
                Log?.Invoke($"Could not remove {relativePath}: {exception.Message}");
                return false;
            }
        }

        private void SaveState(Manifest manifest, List<InstalledEntry> files)
        {
            var state = new InstalledState
            {
                Channel = manifest.Channel,
                Version = manifest.Version,
                BetaCode = BetaCode,
            };

            state.Reset(files.OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase));
            ManifestService.SaveInstalledState(_statePath, state);
        }

        private static InstalledEntry CreateEntry(string path, FileInfo file, string hash) => new()
        {
            Path = path,
            Size = file.Length,
            ModifiedUtc = file.LastWriteTimeUtc,
            Sha256 = hash,
        };

        public static string FormatSize(long bytes)
        {
            // Unites anglaises : c'est la langue de l'interface.
            string[] units = ["B", "KB", "MB", "GB", "TB"];
            double value = bytes;
            int unit = 0;

            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return unit == 0
                ? $"{bytes} B"
                : string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "{0:0.#} {1}",
                    value,
                    units[unit]);
        }

        private static string FormatSpeed(double bytesPerSecond)
        {
            return bytesPerSecond <= 0 ? "..." : $"{FormatSize((long)bytesPerSecond)}/s";
        }
    }
}
