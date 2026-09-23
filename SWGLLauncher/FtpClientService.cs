using FluentFTP;

namespace SWGLLauncher
{
    /// <summary>
    /// Connexion FTP(S) au depot de fichiers.
    /// Le canal public utilise un compte fixe ; une beta utilise le compte
    /// "&lt;prefixe&gt;&lt;code&gt;" dont le mot de passe est le code lui-meme.
    /// </summary>
    internal sealed class FtpClientService : IAsyncDisposable
    {
        private readonly LauncherConfig _config;
        private AsyncFtpClient? _client;

        // Canal de la derniere connexion, pour pouvoir se reconnecter a l'identique.
        private string? _betaCode;

        public FtpClientService(LauncherConfig config) => _config = config;

        /// <summary>Nom du compte utilise pour la connexion courante.</summary>
        public string UserName { get; private set; } = string.Empty;

        /// <summary>
        /// Journal : recoit les avertissements et erreurs du protocole, ou tout le trafic
        /// si log.ftp.verbose=true. Peut etre appele depuis les fils de FluentFTP.
        /// </summary>
        public Action<string>? Log { get; init; }

        /// <summary>
        /// Se connecte au canal demande.
        /// </summary>
        /// <param name="betaCode">Code de beta, ou null/vide pour le canal public.</param>
        public async Task ConnectAsync(string? betaCode, CancellationToken cancellationToken)
        {
            string host = _config.GetString("ftp.host", LauncherConfig.DefaultFtpHost);
            if (host.Length == 0)
            {
                throw new InvalidOperationException(
                    "No FTP server configured (ftp.host is empty in launcher.properties).");
            }

            string user;
            string password;

            if (string.IsNullOrWhiteSpace(betaCode))
            {
                user = _config.GetString("ftp.public.user", "swgl-public");
                password = _config.GetString("ftp.public.password", "swgl-public");
            }
            else
            {
                // Le code de beta sert a la fois d'identifiant et de mot de passe :
                // qui connait le code a de toute facon acces a la beta.
                string code = betaCode.Trim();
                user = _config.GetString("ftp.beta.user.prefix", "swgl-") + code;
                password = code;
            }

            int timeout = _config.GetInt("ftp.timeout.ms", 30000);

            var settings = new FtpConfig
            {
                EncryptionMode = ParseEncryptionMode(_config.GetString("ftp.tls", "explicit")),
                ValidateAnyCertificate = _config.GetBool("ftp.accept.any.certificate", true),
                ConnectTimeout = timeout,
                ReadTimeout = timeout,
                DataConnectionConnectTimeout = timeout,
                DataConnectionReadTimeout = timeout,
                RetryAttempts = 3,

                // Pendant un long telechargement, la connexion de controle ne transporte
                // rien : un routeur ou un pare-feu la coupe comme inactive, et la fin du
                // transfert n'est jamais confirmee. NOOP et keep-alive TCP l'entretiennent.
                NoopInterval = _config.GetInt("ftp.noop.interval.ms", 30000),
                SocketKeepAlive = true,
            };

            await DisposeAsync();

            _client = new AsyncFtpClient(
                host, user, password, _config.GetInt("ftp.port", 21), settings);

            if (Log is not null)
            {
                bool verbose = _config.GetBool("log.ftp.verbose", false);

                _client.LegacyLogger = (level, message) =>
                {
                    if (!verbose && level is not (FtpTraceLevel.Warn or FtpTraceLevel.Error))
                    {
                        return;
                    }

                    // Par precaution : le mot de passe d'une beta est son code.
                    string line = message.TrimEnd();
                    if (line.Contains("PASS ", StringComparison.OrdinalIgnoreCase))
                    {
                        line = "> PASS ***";
                    }

                    Log($"FTP {level}: {line}");
                };
            }

            await _client.Connect(cancellationToken);
            UserName = user;
            _betaCode = betaCode;
        }

        /// <summary>
        /// Rouvre une connexion neuve sur le meme canal, apres une coupure : une connexion
        /// de controle qui a expire ne se recupere pas.
        /// </summary>
        public Task ReconnectAsync(CancellationToken cancellationToken)
        {
            return ConnectAsync(_betaCode, cancellationToken);
        }

        /// <summary>Telecharge et analyse le manifeste du canal connecte.</summary>
        public async Task<Manifest> DownloadManifestAsync(CancellationToken cancellationToken)
        {
            AsyncFtpClient client = RequireClient();
            string remotePath = _config.GetString("manifest.file", "/manifest.json");

            byte[]? json = await client.DownloadBytes(remotePath, cancellationToken);

            if (json is null || json.Length == 0)
            {
                throw new InvalidDataException(
                    $"Manifest not found or empty on the server ({remotePath}).");
            }

            return ManifestService.ParseManifest(json);
        }

        /// <summary>
        /// Telecharge un fichier, en reprenant un telechargement partiel si le fichier
        /// local existe deja (commande FTP REST).
        /// </summary>
        public async Task DownloadFileAsync(
            string remotePath,
            string localPath,
            IProgress<FtpProgress>? progress,
            CancellationToken cancellationToken)
        {
            AsyncFtpClient client = RequireClient();

            FtpStatus status = await client.DownloadFile(
                localPath,
                remotePath,
                FtpLocalExists.Resume,
                FtpVerify.None,
                progress,
                cancellationToken);

            if (status == FtpStatus.Failed)
            {
                throw new IOException($"Download failed: {remotePath}.");
            }
        }

        private AsyncFtpClient RequireClient()
        {
            return _client ?? throw new InvalidOperationException("FTP is not connected.");
        }

        private static FtpEncryptionMode ParseEncryptionMode(string value)
        {
            return value.Trim().ToLowerInvariant() switch
            {
                "none" => FtpEncryptionMode.None,
                "implicit" => FtpEncryptionMode.Implicit,
                "auto" => FtpEncryptionMode.Auto,
                _ => FtpEncryptionMode.Explicit,
            };
        }

        public async ValueTask DisposeAsync()
        {
            if (_client is null)
            {
                return;
            }

            try
            {
                await _client.Disconnect();
            }
            catch
            {
                // La connexion est peut-etre deja tombee : rien a sauver ici.
            }

            _client.Dispose();
            _client = null;
        }
    }
}
