using System.Text.Json.Serialization;

namespace SWGLLauncher
{
    /// <summary>
    /// Description complete de l'etat attendu d'une installation (canal public ou beta).
    /// Le manifeste decrit l'etat final, pas une suite d'operations : le launcher peut donc
    /// installer, reparer, mettre a jour, changer de beta ou revenir au public avec le meme
    /// algorithme.
    /// </summary>
    internal sealed class Manifest
    {
        /// <summary>Nom du canal, par exemple "public" ou "beta_k7mrx4pq".</summary>
        [JsonPropertyName("channel")]
        public string Channel { get; set; } = string.Empty;

        /// <summary>Version affichee a l'utilisateur, libre.</summary>
        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        /// <summary>Note de version affichee dans le launcher (optionnelle).</summary>
        [JsonPropertyName("notes")]
        public string Notes { get; set; } = string.Empty;

        /// <summary>Liste complete des fichiers geres par le launcher pour ce canal.</summary>
        [JsonPropertyName("files")]
        public List<ManifestEntry> Files { get; set; } = [];
    }

    internal sealed class ManifestEntry
    {
        /// <summary>Chemin relatif au dossier d'installation, separateurs "/".</summary>
        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;

        /// <summary>Chemin absolu du fichier sur le FTP, tel que vu par le compte connecte.</summary>
        [JsonPropertyName("from")]
        public string From { get; set; } = string.Empty;

        [JsonPropertyName("size")]
        public long Size { get; set; }

        /// <summary>Empreinte SHA-256 en hexadecimal minuscule.</summary>
        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; } = string.Empty;

        /// <summary>Chemin FTP effectif : "from" s'il est renseigne, sinon "path".</summary>
        [JsonIgnore]
        public string RemotePath => From.Length > 0 ? From : "/" + Path;
    }
}
