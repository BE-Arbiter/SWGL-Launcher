using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace SWGLLauncher
{
    /// <summary>Nouvelle version du launcher publiee sur GitHub.</summary>
    internal sealed record LauncherRelease(Version Version, string Tag, string DownloadUrl, long Size, string Sha256);

    /// <summary>
    /// Mise a jour du launcher lui-meme depuis les releases GitHub. La nouvelle version est
    /// telechargee en "*.new" puis lancee ; une fois l'ancienne fermee, elle se copie a sa
    /// place et la relance.
    /// </summary>
    internal sealed class SelfUpdater
    {
        private const string OldSuffix = ".old";
        private const string NewSuffix = ".new";
        private const string ApplyArgument = "--apply-update";

        private static readonly HttpClient Http = CreateClient();

        private readonly LauncherConfig _config;

        public SelfUpdater(LauncherConfig config) => _config = config;

        public Action<string>? Log { get; init; }

        /// <summary>Executable en cours d'execution.</summary>
        public static string ExecutablePath => Environment.ProcessPath ?? Application.ExecutablePath;

        /// <summary>Version de l'executable en cours, sur trois composantes.</summary>
        public static Version CurrentVersion => Normalize(
            Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0));

        /// <summary>
        /// Interroge la derniere release. Null si le launcher est a jour, si la release ne
        /// contient pas l'executable attendu, ou si GitHub ne repond pas : la mise a jour du
        /// launcher ne doit jamais empecher de jouer.
        /// </summary>
        public async Task<LauncherRelease?> FindUpdateAsync(CancellationToken cancellationToken)
        {
            string repository = _config.GetString("update.repository", "BE-Arbiter/SWGL-Launcher");
            string api = _config.GetString("update.api", "https://api.github.com").TrimEnd('/');
            string assetName = _config.GetString("update.asset", "SWGLLauncher.exe");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(_config.GetInt("update.timeout.seconds", 10)));

            using var request = new HttpRequestMessage(
                HttpMethod.Get, $"{api}/repos/{repository}/releases/latest");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using HttpResponseMessage response = await Http.SendAsync(request, timeout.Token);

            if (!response.IsSuccessStatusCode)
            {
                Log?.Invoke($"Launcher update check: HTTP {(int)response.StatusCode}");
                return null;
            }

            using JsonDocument json = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(timeout.Token));

            JsonElement root = json.RootElement;
            string tag = root.GetProperty("tag_name").GetString() ?? string.Empty;

            // "v1.2.0" ou "v0.2.0-alpha" : seules les trois composantes comptent.
            string number = tag.TrimStart('v', 'V').Split('-', '+')[0];

            if (!Version.TryParse(number, out Version? published))
            {
                Log?.Invoke($"Launcher update check: unreadable version \"{tag}\"");
                return null;
            }

            if (Normalize(published) <= CurrentVersion)
            {
                return null;
            }

            foreach (JsonElement asset in root.GetProperty("assets").EnumerateArray())
            {
                if (!string.Equals(asset.GetProperty("name").GetString(), assetName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // GitHub publie l'empreinte de chaque fichier attache : "sha256:<hex>".
                string digest = asset.TryGetProperty("digest", out JsonElement d) && d.ValueKind == JsonValueKind.String
                    ? d.GetString()!
                    : string.Empty;

                return new LauncherRelease(
                    Normalize(published),
                    tag,
                    asset.GetProperty("browser_download_url").GetString() ?? string.Empty,
                    asset.GetProperty("size").GetInt64(),
                    digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..] : string.Empty);
            }

            Log?.Invoke($"Launcher {tag} is published without {assetName}: ignored");
            return null;
        }

        /// <summary>
        /// Telecharge la nouvelle version a cote de l'executable ("*.new") et verifie sa
        /// taille et son empreinte. L'executable en cours n'est pas touche.
        /// </summary>
        public async Task DownloadAsync(
            LauncherRelease release, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            string downloaded = ExecutablePath + NewSuffix;

            try
            {
                using (HttpResponseMessage response = await Http.GetAsync(
                    release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                {
                    response.EnsureSuccessStatusCode();

                    await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
                    await using var target = new FileStream(downloaded, FileMode.Create, FileAccess.Write, FileShare.None);

                    byte[] buffer = new byte[1024 * 1024];
                    long total = 0;
                    int read;

                    while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        total += read;
                        progress?.Report(release.Size > 0 ? total / (double)release.Size : -1);
                    }
                }

                long size = new FileInfo(downloaded).Length;
                if (release.Size > 0 && size != release.Size)
                {
                    throw new InvalidDataException($"size {size} instead of {release.Size}");
                }

                if (release.Sha256.Length > 0)
                {
                    string hash = await ManifestService.ComputeSha256Async(downloaded, cancellationToken);

                    if (!hash.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException("checksum mismatch");
                    }
                }
                else
                {
                    Log?.Invoke("No checksum published for this release: size check only");
                }
            }
            catch
            {
                TryDelete(downloaded);
                throw;
            }
        }

        /// <summary>
        /// Lance la version telechargee pour qu'elle prenne la place de l'executable courant,
        /// qui doit ensuite se fermer. On ne renomme ni n'ecrase jamais un executable en cours :
        /// un exe "fichier unique" va chercher ses bibliotheques dans son propre fichier au fil
        /// de l'eau, et plante des qu'il n'est plus a sa place.
        /// </summary>
        public static void StartInstaller()
        {
            var start = new ProcessStartInfo(ExecutablePath + NewSuffix)
            {
                UseShellExecute = false,
                WorkingDirectory = Environment.CurrentDirectory,
            };

            start.ArgumentList.Add(ApplyArgument);
            start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));

            Process.Start(start);
        }

        /// <summary>
        /// Seconde moitie de la mise a jour, executee par "SWGLLauncher.exe.new --apply-update
        /// &lt;pid&gt;" : attend la fermeture de l'ancienne version, se copie a sa place, la
        /// relance et rend la main. Faux si le programme n'a pas ete lance ainsi.
        /// </summary>
        public static bool TryApplyUpdate(string[] args)
        {
            if (args.Length != 2 || args[0] != ApplyArgument)
            {
                return false;
            }

            string self = ExecutablePath;

            // La cible se deduit de notre propre nom, jamais d'un argument : cette option ne
            // peut ecraser que le launcher a cote duquel elle a ete telechargee.
            if (!self.EndsWith(NewSuffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string target = self[..^NewSuffix.Length];

            if (int.TryParse(args[1], out int pid))
            {
                try
                {
                    using Process previous = Process.GetProcessById(pid);
                    previous.WaitForExit(30_000);
                }
                catch (ArgumentException)
                {
                    // Deja fermee.
                }
            }

            // L'antivirus peut garder la cible ouverte un instant apres la fermeture.
            for (int attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    File.Copy(self, target, overwrite: true);
                    break;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    Thread.Sleep(500);
                }
            }

            // Copie reussie ou non, on relance le launcher : au pire l'ancienne version, qui
            // retentera la mise a jour au prochain demarrage.
            Process.Start(new ProcessStartInfo(target)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(target)!,
            });

            return true;
        }

        /// <summary>
        /// Supprime les restes d'une mise a jour : le ".new" qui vient de se copier (il peut
        /// mettre un instant a se fermer) et le ".old" des versions precedentes.
        /// </summary>
        public static async Task CleanUpAsync()
        {
            foreach (string leftover in new[] { ExecutablePath + NewSuffix, ExecutablePath + OldSuffix })
            {
                for (int attempt = 0; attempt < 20 && File.Exists(leftover); attempt++)
                {
                    if (TryDelete(leftover))
                    {
                        break;
                    }

                    await Task.Delay(500);
                }
            }
        }

        private static bool TryDelete(string path)
        {
            try
            {
                File.Delete(path);
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static Version Normalize(Version version) =>
            new(version.Major, version.Minor, Math.Max(0, version.Build));

        private static HttpClient CreateClient()
        {
            var client = new HttpClient();

            // L'API GitHub refuse les requetes sans User-Agent.
            client.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("SWGLLauncher", CurrentVersion.ToString()));
            return client;
        }
    }
}
