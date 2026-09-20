using System.Diagnostics;
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
            var manifest = new Manifest
            {
                Channel = options.Channel,
                Version = options.Version,
                Notes = options.Notes,
            };

            var stopwatch = Stopwatch.StartNew();
            var entries = new Dictionary<string, ManifestEntry>(StringComparer.OrdinalIgnoreCase);

            // Le dossier commun d'abord : les fichiers de la beta l'emportent ensuite.
            if (options.BaseDirectory is not null)
            {
                AddDirectory(entries, options.BaseDirectory, options.BasePrefix, options);
            }

            AddDirectory(entries, options.Source, options.SourcePrefix, options);

            manifest.Files = [.. entries.Values.OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase)];

            File.WriteAllText(options.Output, ManifestService.Serialize(manifest));

            long total = manifest.Files.Sum(file => file.Size);
            Console.WriteLine();
            Console.WriteLine($"Canal    : {manifest.Channel}");
            Console.WriteLine($"Version  : {manifest.Version}");
            Console.WriteLine($"Fichiers : {manifest.Files.Count} ({FormatSize(total)})");
            Console.WriteLine($"Duree    : {stopwatch.Elapsed:mm\\:ss}");
            Console.WriteLine($"Ecrit    : {options.Output}");

            return 0;
        }

        private static void AddDirectory(
            Dictionary<string, ManifestEntry> entries,
            string directory,
            string remotePrefix,
            Options options)
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

                var info = new FileInfo(file);
                Console.WriteLine($"  {relative} ({FormatSize(info.Length)})");

                entries[relative] = new ManifestEntry
                {
                    Path = relative,
                    From = CombineRemote(remotePrefix, relative),
                    Size = info.Length,
                    Sha256 = ManifestService
                        .ComputeSha256Async(file, CancellationToken.None)
                        .GetAwaiter()
                        .GetResult(),
                };
            }
        }

        private static bool IsExcluded(string relativePath, Options options)
        {
            string name = Path.GetFileName(relativePath);

            if (name.Equals(Path.GetFileName(options.Output), StringComparison.OrdinalIgnoreCase)
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

        /// <summary>Arguments de la ligne de commande.</summary>
        private sealed class Options
        {
            public string Source { get; private set; } = string.Empty;
            public string SourcePrefix { get; private set; } = "/";
            public string? BaseDirectory { get; private set; }
            public string BasePrefix { get; private set; } = "/base";
            public string Channel { get; private set; } = "public";
            public string Version { get; private set; } = DateTime.Now.ToString("yyyy.MM.dd.HHmm");
            public string Notes { get; private set; } = string.Empty;
            public string Output { get; private set; } = string.Empty;
            public List<string> Exclude { get; } = [];

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
                        case "--channel": options.Channel = Next(); break;
                        case "--version": options.Version = Next(); break;
                        case "--notes": options.Notes = Next(); break;
                        case "--output": options.Output = Next(); break;
                        case "--exclude": options.Exclude.Add(Next()); break;
                        default: throw new ArgumentException($"Argument inconnu : {key}");
                    }
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
                      --channel <nom>            Nom du canal (defaut : public)
                      --version <texte>          Version affichee (defaut : date du jour)
                      --notes <texte>            Note de version
                      --output <fichier>         Fichier a ecrire (defaut : <source>/manifest.json)
                      --exclude <fragment>       Exclut les chemins contenant ce fragment

                    Exemples :
                      SWGLManifest --source C:\ftp\public --channel public
                      SWGLManifest --source C:\ftp\betas\k7mrx4pq --base C:\ftp\base ^
                                   --channel beta_k7mrx4pq --version "Ep3 test 3"
                    """);
            }
        }
    }
}
