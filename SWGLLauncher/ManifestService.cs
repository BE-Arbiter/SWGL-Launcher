using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SWGLLauncher
{
    /// <summary>
    /// Lecture / ecriture des manifestes et de l'etat local, plus les utilitaires
    /// associes (empreintes, validation des chemins).
    /// </summary>
    internal static class ManifestService
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
        };

        public static Manifest ParseManifest(byte[] json)
        {
            Manifest? manifest = JsonSerializer.Deserialize<Manifest>(
                Encoding.UTF8.GetString(json).TrimStart('﻿'), Options);

            if (manifest is null)
            {
                throw new InvalidDataException("The manifest is empty or unreadable.");
            }

            foreach (ManifestEntry entry in manifest.Files)
            {
                entry.Path = NormalizeRelativePath(entry.Path);
            }

            return manifest;
        }

        public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

        public static InstalledState LoadInstalledState(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return new InstalledState();
            }

            try
            {
                InstalledState? state = JsonSerializer.Deserialize<InstalledState>(
                    File.ReadAllText(filePath), Options);

                return state ?? new InstalledState();
            }
            catch
            {
                // Etat corrompu : on repart d'une base vide, la verification par empreinte
                // reconstruira l'information.
                return new InstalledState();
            }
        }

        public static void SaveInstalledState(string filePath, InstalledState state)
        {
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(filePath, Serialize(state));
        }

        public static async Task<string> ComputeSha256Async(
            string filePath, CancellationToken cancellationToken)
        {
            await using FileStream stream = new(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1024 * 1024,
                useAsync: true);

            byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        /// <summary>Normalise un chemin de manifeste : separateurs "/", sans "./" ni doublons.</summary>
        public static string NormalizeRelativePath(string path)
        {
            return path.Replace('\\', '/').Trim().TrimStart('/');
        }

        /// <summary>
        /// Combine le dossier d'installation et un chemin issu du manifeste, en refusant
        /// tout ce qui sortirait du dossier (chemin absolu, remontee par "..").
        /// Le manifeste vient du reseau : il ne doit jamais pouvoir ecrire ailleurs.
        /// </summary>
        public static string ResolveLocalPath(string installRoot, string relativePath)
        {
            string normalized = NormalizeRelativePath(relativePath);

            if (normalized.Length == 0)
            {
                throw new InvalidDataException("Empty path in the manifest.");
            }

            if (Path.IsPathRooted(normalized)
                || normalized.Split('/').Any(segment => segment == ".."))
            {
                throw new InvalidDataException($"Manifest path refused: \"{relativePath}\".");
            }

            string root = Path.GetFullPath(installRoot);
            string full = Path.GetFullPath(Path.Combine(root, normalized));

            if (!full.StartsWith(
                    root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Manifest path refused: \"{relativePath}\".");
            }

            return full;
        }
    }
}
