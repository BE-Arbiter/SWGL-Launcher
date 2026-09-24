using System.Diagnostics;
using System.Text.Json;
using SWGLLauncher;

namespace SWGLLauncher.ManifestTool
{
    /// <summary>
    /// Generateur de manifeste : scanne un dossier (et, pour une beta, le dossier "base"
    /// partage), calcule les empreintes et ecrit le manifest.json a deposer sur le FTP.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                Options options = Options.Parse(args);
                return Run(options);
            }
            catch (ArgumentException exception)
            {
                Console.Error.WriteLine($"Erreur : {exception.Message}");
                Console.Error.WriteLine();
                Options.PrintUsage();
                return 1;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"Erreur : {exception.Message}");
                return 2;
            }
        }

        private static int Run(Options options)
        {
            // Manifeste deja publie : son libelle est conserve si aucun n'est donne.
            Manifest? previous = TryLoadManifest(options.Output);

            if (options.Relabel)
            {
                return Relabel(options, previous);
            }

            if (options.NotifyDirectory is not null)
            {
                return Notifier.Publish(
                    options.NotifyDirectory, options.Message, options.Pings, options.ConfigPath, options.RolesPath);
            }

            var manifest = new Manifest
            {
                Channel = options.Channel,
                Version = options.Version ?? previous?.Version ?? DefaultVersion(),
                Notes = options.Notes,
            };

            var stopwatch = Stopwatch.StartNew();
            var entries = new Dictionary<string, ManifestEntry>(StringComparer.OrdinalIgnoreCase);
            var inheritedDeletions = new List<string>();

            // Empreintes du manifeste precedent, reprises pour tout fichier dont la taille et la
            // date n'ont pas bouge : seuls les fichiers nouveaux ou modifies sont relus.
            var known = options.Rehash || previous is null
                ? new Dictionary<string, ManifestEntry>(StringComparer.OrdinalIgnoreCase)
                : previous.Files
                    .Where(file => file.Modified is not null)
                    .GroupBy(file => file.From, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var stats = new HashStats();

            string[] removals = LoadPatternList(options.RemoveList, "Liste de retrait");
            bool[] removalUsed = new bool[removals.Length];

            // Le dossier commun d'abord : les fichiers de la beta l'emportent ensuite.
            // La liste de retrait ne s'applique qu'a lui : un fichier depose dans la beta
            // elle-meme est voulu, il reste. Si le manifeste public est fourni, il remplace
            // le parcours du dossier commun : ses empreintes sont deja calculees.
            if (options.BaseManifest is not null)
            {
                inheritedDeletions.AddRange(AddManifest(entries, options.BaseManifest, removals, removalUsed));
            }
            else if (options.BaseDirectory is not null)
            {
                AddDirectory(entries, options.BaseDirectory, options.BasePrefix, options, removals, removalUsed, known, stats);
            }

            AddDirectory(entries, options.Source, options.SourcePrefix, options, [], [], known, stats);

            // Un motif qui ne retire rien est presque toujours une faute de frappe.
            for (int i = 0; i < removals.Length; i++)
            {
                if (!removalUsed[i])
                {
                    Console.Error.WriteLine($"Attention : \"{removals[i]}\" ne correspond a aucun fichier de base.");
                }
            }

            manifest.Files = [.. entries.Values.OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase)];

            // Fichiers que le launcher doit supprimer chez le joueur, hors de ses propres motifs.
            // Celles du public valent aussi pour une beta construite sur son manifeste.
            manifest.Delete = [.. inheritedDeletions
                .Concat(LoadPatternList(options.DeleteList, "Liste de suppression"))
                .Distinct(StringComparer.OrdinalIgnoreCase)];

            // Le launcher ne supprime jamais un fichier publie : le motif serait sans effet.
            foreach (string pattern in manifest.Delete)
            {
                foreach (ManifestEntry published in manifest.Files.Where(
                    file => ManifestService.MatchesPattern(file.Path, pattern)))
                {
                    Console.Error.WriteLine(
                        $"Attention : {published.Path} est publie par ce canal, \"{pattern}\" ne le supprimera pas.");
                }
            }

            File.WriteAllText(options.Output, ManifestService.Serialize(manifest));

            // Ce qui a change depuis le manifeste precedent, pour l'annonce Discord.
            if (options.ChangesFile is not null)
            {
                File.WriteAllText(options.ChangesFile, JsonSerializer.Serialize(BranchChanges.Compare(previous, manifest)));
            }

            long total = manifest.Files.Sum(file => file.Size);
            Console.WriteLine();
            Console.WriteLine($"Canal    : {manifest.Channel}");
            Console.WriteLine($"Version  : {manifest.Version}");
            Console.WriteLine($"Fichiers : {manifest.Files.Count} ({FormatSize(total)})");
            Console.WriteLine($"Haches   : {stats.Hashed}, repris sans relecture : {stats.Reused}");

            if (manifest.Delete.Count > 0)
            {
                Console.WriteLine($"A supprimer chez le joueur : {string.Join(", ", manifest.Delete)}");
            }

            Console.WriteLine($"Duree    : {stopwatch.Elapsed:mm\\:ss}");
            Console.WriteLine($"Ecrit    : {options.Output}");

            return 0;
        }

        /// <summary>Change seulement le libelle d'un manifeste existant, sans rien recalculer.</summary>
        private static int Relabel(Options options, Manifest? previous)
        {
            if (previous is null)
            {
                throw new ArgumentException($"Manifeste introuvable ou illisible : {options.Output}");
            }

            if (options.Version is null)
            {
                throw new ArgumentException("--relabel demande --version.");
            }

            string before = previous.Version;
            previous.Version = options.Version;
            File.WriteAllText(options.Output, ManifestService.Serialize(previous));

            Console.WriteLine($"Canal    : {previous.Channel}");
            Console.WriteLine($"Version  : {before} -> {previous.Version}");
            Console.WriteLine($"Ecrit    : {options.Output}");
            return 0;
        }

        private static Manifest? TryLoadManifest(string path)
        {
            try
            {
                return File.Exists(path) ? ManifestService.ParseManifest(File.ReadAllBytes(path)) : null;
            }
            catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException or InvalidDataException)
            {
                return null;
            }
        }

        private static string DefaultVersion() => DateTime.Now.ToString("yyyy.MM.dd.HHmm");

        /// <summary>
        /// Reprend les fichiers d'un manifeste deja genere (le public), sans relire ni hacher
        /// le dossier commun. La liste de retrait s'applique comme pour un dossier.
        /// </summary>
        /// <returns>Les suppressions forcees de ce manifeste, heritees par la beta.</returns>
        private static IReadOnlyList<string> AddManifest(
            Dictionary<string, ManifestEntry> entries,
            string manifestPath,
            string[] removals,
            bool[] removalUsed)
        {
            Manifest source = TryLoadManifest(manifestPath)
                ?? throw new ArgumentException($"Manifeste de base introuvable ou illisible : {manifestPath}");

            foreach (ManifestEntry entry in source.Files)
            {
                if (IsRemoved(entry.Path, removals, removalUsed))
                {
                    Console.WriteLine($"  - {entry.Path} (retire)");
                    continue;
                }

                entries[entry.Path] = entry;
            }

            Console.WriteLine($"  base : {source.Files.Count} fichier(s) repris de {manifestPath} ({source.Version})");
            return source.Delete;
        }

        /// <summary>
        /// Vrai si un motif de la liste de retrait couvre le chemin. Tous les motifs qui le
        /// couvrent sont marques utilises, pas seulement le premier.
        /// </summary>
        private static bool IsRemoved(string relative, string[] removals, bool[] removalUsed)
        {
            bool removed = false;

            for (int i = 0; i < removals.Length; i++)
            {
                if (ManifestService.MatchesPattern(relative, removals[i]))
                {
                    removalUsed[i] = true;
                    removed = true;
                }
            }

            return removed;
        }

        /// <summary>
        /// Lit une liste de chemins : un chemin ou un motif par ligne, lignes vides et
        /// commentaires "#" ignores.
        /// </summary>
        private static string[] LoadPatternList(string? path, string description)
        {
            if (path is null)
            {
                return [];
            }

            if (!File.Exists(path))
            {
                throw new ArgumentException($"{description} introuvable : {path}");
            }

            return [.. File.ReadAllLines(path)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith('#'))
                .Select(ManifestService.NormalizeRelativePath)];
        }

        private static void AddDirectory(
            Dictionary<string, ManifestEntry> entries,
            string directory,
            string remotePrefix,
            Options options,
            string[] removals,
            bool[] removalUsed,
            IReadOnlyDictionary<string, ManifestEntry> known,
            HashStats stats)
        {
            string root = Path.GetFullPath(directory);

            if (!Directory.Exists(root))
            {
                throw new ArgumentException($"Dossier introuvable : {root}");
            }

            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                string relative = ManifestService.NormalizeRelativePath(
                    Path.GetRelativePath(root, file));

                if (IsExcluded(relative, options))
                {
                    continue;
                }

                // Retire avant de hacher : inutile de lire un fichier qui ne sera pas publie.
                if (IsRemoved(relative, removals, removalUsed))
                {
                    Console.WriteLine($"  - {relative} (retire)");
                    continue;
                }

                var info = new FileInfo(file);
                string from = CombineRemote(remotePrefix, relative);
                DateTime modified = info.LastWriteTimeUtc;

                // Meme fichier, meme taille et meme date qu'au dernier calcul, a l'identique :
                // l'empreinte est reprise. Au moindre ecart, le fichier est relu.
                string hash;

                if (known.TryGetValue(from, out ManifestEntry? previous)
                    && previous.Size == info.Length
                    && previous.Modified == modified
                    && previous.Sha256.Length > 0)
                {
                    hash = previous.Sha256;
                    stats.Reused++;
                }
                else
                {
                    Console.WriteLine($"  {relative} ({FormatSize(info.Length)})");
                    hash = ManifestService
                        .ComputeSha256Async(file, CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();
                    stats.Hashed++;
                }

                entries[relative] = new ManifestEntry
                {
                    Path = relative,
                    From = from,
                    Size = info.Length,
                    Sha256 = hash,
                    Modified = modified,
                };
            }
        }

        private static bool IsExcluded(string relativePath, Options options)
        {
            string name = Path.GetFileName(relativePath);

            // Le manifeste d'un canal vit a la racine de son dossier : il ne doit jamais se
            // retrouver liste, quel que soit le nom du fichier que l'on est en train d'ecrire.
            if (name.Equals(Path.GetFileName(options.Output), StringComparison.OrdinalIgnoreCase)
                || relativePath.Equals("manifest.json", StringComparison.OrdinalIgnoreCase)
                || name.Equals("installed.json", StringComparison.OrdinalIgnoreCase)
                || relativePath.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return options.Exclude.Any(pattern =>
                relativePath.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        }

        private static string CombineRemote(string prefix, string relativePath)
        {
            string cleaned = prefix.Trim().Replace('\\', '/').TrimEnd('/');

            if (cleaned.Length == 0)
            {
                cleaned = string.Empty;
            }
            else if (!cleaned.StartsWith('/'))
            {
                cleaned = "/" + cleaned;
            }

            return $"{cleaned}/{relativePath}";
        }

        private static string FormatSize(long bytes)
        {
            string[] units = ["o", "Ko", "Mo", "Go", "To"];
            double value = bytes;
            int unit = 0;

            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return unit == 0 ? $"{bytes} o" : $"{value:0.#} {units[unit]}";
        }

        /// <summary>Compteurs affiches en fin de generation.</summary>
        private sealed class HashStats
        {
            public int Hashed { get; set; }

            public int Reused { get; set; }
        }

        /// <summary>Arguments de la ligne de commande.</summary>
        private sealed class Options
        {
            public string Source { get; private set; } = string.Empty;
            public string SourcePrefix { get; private set; } = "/";
            public string? BaseDirectory { get; private set; }
            public string BasePrefix { get; private set; } = "/base";
            public string Channel { get; private set; } = "public";
            public string? Version { get; private set; }
            public string? BaseManifest { get; private set; }
            public bool Rehash { get; private set; }
            public string? ChangesFile { get; private set; }
            public string? NotifyDirectory { get; private set; }
            public string? Message { get; private set; }
            public List<string> Pings { get; } = [];
            public string ConfigPath { get; private set; } = "/etc/swgl-sync.conf";
            public string? RolesPath { get; private set; }
            public bool Relabel { get; private set; }
            public string Notes { get; private set; } = string.Empty;
            public string Output { get; private set; } = string.Empty;
            public List<string> Exclude { get; } = [];
            public string? RemoveList { get; private set; }
            public string? DeleteList { get; private set; }

            public static Options Parse(string[] args)
            {
                if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
                {
                    throw new ArgumentException("Arguments manquants.");
                }

                var options = new Options();

                for (int i = 0; i < args.Length; i++)
                {
                    string key = args[i];
                    string Next()
                    {
                        if (i + 1 >= args.Length)
                        {
                            throw new ArgumentException($"Valeur manquante apres {key}.");
                        }

                        return args[++i];
                    }

                    switch (key)
                    {
                        case "--source": options.Source = Next(); break;
                        case "--source-prefix": options.SourcePrefix = Next(); break;
                        case "--base": options.BaseDirectory = Next(); break;
                        case "--base-prefix": options.BasePrefix = Next(); break;
                        case "--base-manifest": options.BaseManifest = Next(); break;
                        case "--relabel": options.Relabel = true; break;
                        case "--rehash": options.Rehash = true; break;
                        case "--changes": options.ChangesFile = Next(); break;
                        case "--notify": options.NotifyDirectory = Next(); break;
                        case "--message": options.Message = Next(); break;
                        case "--ping": options.Pings.Add(Next()); break;
                        case "--config": options.ConfigPath = Next(); break;
                        case "--roles": options.RolesPath = Next(); break;
                        case "--channel": options.Channel = Next(); break;
                        case "--version": options.Version = Next(); break;
                        case "--notes": options.Notes = Next(); break;
                        case "--output": options.Output = Next(); break;
                        case "--exclude": options.Exclude.Add(Next()); break;
                        case "--remove-list": options.RemoveList = Next(); break;
                        case "--delete-list": options.DeleteList = Next(); break;
                        default: throw new ArgumentException($"Argument inconnu : {key}");
                    }
                }

                if (options.NotifyDirectory is not null)
                {
                    return options;
                }

                if (options.Relabel)
                {
                    if (options.Output.Length == 0)
                    {
                        throw new ArgumentException("--relabel demande --output.");
                    }

                    return options;
                }

                if (options.Source.Length == 0)
                {
                    throw new ArgumentException("--source est obligatoire.");
                }

                if (options.Output.Length == 0)
                {
                    options.Output = Path.Combine(options.Source, "manifest.json");
                }

                return options;
            }

            public static void PrintUsage()
            {
                Console.Error.WriteLine("""
                    SWGLManifest — genere le manifest.json d'un canal.

                      --source <dossier>         Dossier des fichiers du canal (obligatoire)
                      --source-prefix <chemin>   Chemin FTP de ce dossier (defaut : /)
                      --base <dossier>           Dossier commun partage (optionnel, pour une beta)
                      --base-prefix <chemin>     Chemin FTP du dossier commun (defaut : /base)
                      --base-manifest <fichier>  Manifeste deja genere du dossier commun (le public) :
                                                 ses fichiers sont repris sans etre relus ni haches
                      --channel <nom>            Nom du canal (defaut : public)
                      --version <texte>          Version affichee (defaut : celle du manifeste
                                                 existant, sinon la date du jour)
                      --notes <texte>            Note de version
                      --output <fichier>         Fichier a ecrire (defaut : <source>/manifest.json)
                      --exclude <fragment>       Exclut les chemins contenant ce fragment
                      --remove-list <fichier>    Fichiers de --base a retirer : un chemin ou un
                                                 motif par ligne ("*" dans un dossier, "**" au-dela)
                      --delete-list <fichier>    Fichiers a supprimer chez le joueur meme hors des
                                                 motifs du launcher, meme format
                      --relabel                  Change seulement la version de --output, sans rien
                                                 recalculer (avec --version)
                      --rehash                   Relit et hache tous les fichiers, sans reprendre les
                                                 empreintes du manifeste existant
                      --changes <fichier>        Ecrit ce qui a change depuis le manifeste existant (JSON)

                    Annonce Discord des branches modifiees (au lieu de generer) :
                      --notify <dossier>         Dossier des fichiers --changes, un "<branche>.json" par branche
                      --message <texte>          Texte place en tete, tel quel
                      --ping <role>              Role a mentionner en tete : nom declare dans la configuration
                                                 ou identifiant ; repetable
                      --config <fichier>         webhook=... (defaut : /etc/swgl-sync.conf)
                      --roles <fichier>          Roles mentionnables par leur nom : une ligne <Nom>=<id>

                    Exemples :
                      SWGLManifest --source C:\ftp\public --channel public
                      SWGLManifest --source C:\ftp\betas\k7mrx4pq --base C:\ftp\base ^
                                   --channel beta_k7mrx4pq --version "Ep3 test 3"
                    """);
            }
        }
    }
}
