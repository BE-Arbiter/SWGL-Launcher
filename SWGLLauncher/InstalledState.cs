using System.Text.Json.Serialization;

namespace SWGLLauncher
{
    /// <summary>
    /// Etat local de l'installation : ce que le launcher a reellement pose sur le disque.
    /// Sert a deux choses : eviter de recalculer les empreintes a chaque lancement, et
    /// savoir quels fichiers sont geres par le launcher (donc supprimables).
    /// </summary>
    internal sealed class InstalledState
    {
        [JsonPropertyName("channel")]
        public string Channel { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        /// <summary>Dernier code de beta utilise, pre-rempli au prochain lancement.</summary>
        [JsonPropertyName("betaCode")]
        public string BetaCode { get; set; } = string.Empty;

        [JsonPropertyName("files")]
        public List<InstalledEntry> Files { get; set; } = [];

        [JsonIgnore]
        private Dictionary<string, InstalledEntry>? _index;

        /// <summary>Acces par chemin relatif (insensible a la casse, comme Windows).</summary>
        public InstalledEntry? Find(string path)
        {
            _index ??= Files.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
            return _index.TryGetValue(path, out InstalledEntry? entry) ? entry : null;
        }

        public void Reset(IEnumerable<InstalledEntry> files)
        {
            Files = [.. files];
            _index = null;
        }
    }

    internal sealed class InstalledEntry
    {
        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;

        [JsonPropertyName("size")]
        public long Size { get; set; }

        /// <summary>Date de modification du fichier au moment de l'installation (UTC).</summary>
        [JsonPropertyName("modifiedUtc")]
        public DateTime ModifiedUtc { get; set; }

        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; } = string.Empty;
    }
}
