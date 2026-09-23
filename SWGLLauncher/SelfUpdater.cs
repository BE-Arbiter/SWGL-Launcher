using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace SWGLLauncher
{
    /// <summary>Nouvelle version du launcher publiee sur GitHub.</summary>
    internal sealed record LauncherRelease(Version Version, string Tag, string DownloadUrl, long Size, string Sha256);

    /// <summary>
    /// Mise a jour du launcher lui-meme depuis les releases GitHub. Windows interdit
    /// d'ecraser un executable en cours d'execution, mais pas de le renommer : l'ancien
    /// exe devient "*.old", le nouveau prend sa place, puis le launcher se relance.
    /// </summary>
    internal sealed class SelfUpdater
    {
        private const string OldSuffix = ".old";
        private const string NewSuffix = ".new";

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
        /// Telecharge la nouvelle version, verifie sa taille et son empreinte, puis la met a
        /// la place de l'executable courant. En cas d'echec, l'executable d'origine reste.
        /// </summary>
        public async Task InstallAsync(
            LauncherRelease release, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            string executable = ExecutablePath;
            string downloaded = executable + NewSuffix;
            string previous = executable + OldSuffix;

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

                // Renommer l'exe en cours est permis ; s'il reste un .old d'une mise a jour
                // precedente encore verrouille, on abandonne proprement.
                File.Move(executable, previous, overwrite: true);

                try
                {
                    File.Move(downloaded, executable);
                }
                catch
                {
                    File.Move(previous, executable);
                    throw;
                }
            }
            finally
            {
                TryDelete(downloaded);
            }
        }

        /// <summary>Relance le launcher (la nouvelle version) avec les memes arguments.</summary>
        public static void Restart()
        {
            var start = new ProcessStartInfo(ExecutablePath)
            {
                UseShellExecute = false,
                WorkingDirectory = Environment.CurrentDirectory,
            };

            foreach (string argument in Environment.GetCommandLineArgs().Skip(1))
            {
                start.ArgumentList.Add(argument);
            }

            Process.Start(start);
        }

        /// <summary>
        /// Supprime l'ancien executable laisse par une mise a jour. L'ancienne instance peut
        /// mettre un instant a se fermer : quelques essais espaces.
        /// </summary>
        public static async Task CleanUpAsync()
        {
            string previous = ExecutablePath + OldSuffix;

            for (int attempt = 0; attempt < 20 && File.Exists(previous); attempt++)
            {
                if (TryDelete(previous))
                {
                    return;
                }

                await Task.Delay(500);
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
