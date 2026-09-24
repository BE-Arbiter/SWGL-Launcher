using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SWGLLauncher.ManifestTool
{
    /// <summary>Un fichier ajoute ou modifie par une generation de manifeste.</summary>
    internal sealed class ChangedFile
    {
        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }

    /// <summary>
    /// Ce qu'une generation a change dans le manifeste d'une branche, par rapport au
    /// manifeste precedent. Ecrit par "--changes", lu par "--notify".
    /// </summary>
    internal sealed class BranchChanges
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        /// <summary>Vrai s'il n'y avait pas encore de manifeste pour cette branche.</summary>
        [JsonPropertyName("new")]
        public bool IsNew { get; set; }

        [JsonPropertyName("added")]
        public List<ChangedFile> Added { get; set; } = [];

        [JsonPropertyName("updated")]
        public List<ChangedFile> Updated { get; set; } = [];

        [JsonPropertyName("removed")]
        public List<string> Removed { get; set; } = [];

        /// <summary>Vrai si la liste des suppressions forcees a change.</summary>
        [JsonPropertyName("deletionsChanged")]
        public bool DeletionsChanged { get; set; }

        [JsonIgnore]
        public bool HasChanges =>
            IsNew || Added.Count > 0 || Updated.Count > 0 || Removed.Count > 0 || DeletionsChanged;

        /// <summary>Compare deux manifestes : le contenu seulement, pas le libelle.</summary>
        public static BranchChanges Compare(Manifest? previous, Manifest current)
        {
            var changes = new BranchChanges { Version = current.Version, IsNew = previous is null };
            var before = (previous?.Files ?? [])
                .GroupBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            foreach (ManifestEntry file in current.Files)
            {
                if (!before.Remove(file.Path, out ManifestEntry? old))
                {
                    changes.Added.Add(new ChangedFile { Path = file.Path, Size = file.Size });
                }
                else if (old.Size != file.Size
                    || !old.Sha256.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    changes.Updated.Add(new ChangedFile { Path = file.Path, Size = file.Size });
                }
            }

            changes.Removed = [.. before.Keys.Order(StringComparer.OrdinalIgnoreCase)];

            var deletedBefore = new HashSet<string>(previous?.Delete ?? [], StringComparer.OrdinalIgnoreCase);
            changes.DeletionsChanged = previous is not null && !deletedBefore.SetEquals(current.Delete);

            return changes;
        }
    }

    /// <summary>
    /// Annonce Discord des branches modifiees : un message, par un webhook, avec le detail
    /// fichier par fichier dans un "changes.txt" joint.
    /// </summary>
    internal static class Notifier
    {
        /// <summary>Limite de Discord pour le texte d'un message.</summary>
        private const int MaxContentLength = 2000;

        private const string Public = "public";

        /// <param name="changesDirectory">Un "&lt;branche&gt;.json" par branche regeneree.</param>
        /// <param name="message">Texte libre place en tete, ou null.</param>
        /// <param name="configPath">Fichier cle=valeur : webhook et roles mentionnables.</param>
        public static int Publish(string changesDirectory, string? message, string configPath)
        {
            Dictionary<string, string> config = LoadConfig(configPath);

            if (!config.TryGetValue("webhook", out string? webhook) || webhook.Length == 0)
            {
                throw new ArgumentException($"Aucun webhook dans {configPath} (webhook=https://discord.com/api/webhooks/...).");
            }

            // Le public d'abord, puis les betas par ordre alphabetique.
            var branches = Directory.EnumerateFiles(changesDirectory, "*.json")
                .Select(path => (Name: Path.GetFileNameWithoutExtension(path), Changes: Read(path)))
                .Where(branch => branch.Changes.HasChanges)
                .OrderBy(branch => branch.Name == Public ? 0 : 1)
                .ThenBy(branch => branch.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (branches.Count == 0)
            {
                Console.WriteLine("Notification : aucun manifeste n'a change, rien a annoncer.");
                return 0;
            }

            // Seuls les roles declares dans la configuration peuvent etre mentionnes.
            var roles = config
                .Where(pair => pair.Key.StartsWith("role.", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(pair => pair.Key[5..], pair => pair.Value, StringComparer.OrdinalIgnoreCase);

            var mentioned = new List<string>();
            string header = message is null ? string.Empty : Mention(message.Trim(), roles, mentioned);

            var summary = new StringBuilder();
            var details = new StringBuilder();

            foreach ((string name, BranchChanges changes) in branches)
            {
                string title = $"{(changes.IsNew ? "New branch" : "Branch updated")}: {name}"
                    + (changes.Version.Length > 0 ? $" — {changes.Version}" : string.Empty);

                summary.Append($"**{title}**\n• {Describe(changes)}\n\n");

                details.AppendLine($"== {title}");
                foreach (ChangedFile file in changes.Added)
                {
                    details.AppendLine($"+ {file.Path} ({FormatSize(file.Size)})");
                }

                foreach (ChangedFile file in changes.Updated)
                {
                    details.AppendLine($"~ {file.Path} ({FormatSize(file.Size)})");
                }

                foreach (string path in changes.Removed)
                {
                    details.AppendLine($"- {path}");
                }

                if (changes.DeletionsChanged)
                {
                    details.AppendLine("* forced deletion list changed");
                }

                details.AppendLine();
            }

            string content = header.Length > 0 ? $"{header}\n\n{summary}" : summary.ToString();
            content = Truncate(content.TrimEnd());

            var payload = new
            {
                content,
                allowed_mentions = new { parse = Array.Empty<string>(), roles = mentioned.Distinct().ToArray() },
                attachments = new[] { new { id = 0, filename = "changes.txt" } },
            };

            using var form = new MultipartFormDataContent
            {
                { new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"), "payload_json" },
            };

            var attachment = new ByteArrayContent(Encoding.UTF8.GetBytes(details.ToString()));
            attachment.Headers.ContentType = new MediaTypeHeaderValue("text/plain") { CharSet = "utf-8" };
            form.Add(attachment, "files[0]", "changes.txt");

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            string url = webhook + (webhook.Contains('?') ? "&" : "?") + "wait=true";
            using HttpResponseMessage response = http.PostAsync(url, form).GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
            {
                string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                Console.Error.WriteLine($"Notification refusee par Discord : HTTP {(int)response.StatusCode} {body}");
                return 3;
            }

            Console.WriteLine($"Notification envoyee : {branches.Count} branche(s).");
            return 0;
        }

        private static BranchChanges Read(string path) =>
            JsonSerializer.Deserialize<BranchChanges>(File.ReadAllText(path)) ?? new BranchChanges();

        /// <summary>"3 files added, 1 updated, 2 removed (812 MB to download)".</summary>
        private static string Describe(BranchChanges changes)
        {
            var parts = new List<string>();

            if (changes.Added.Count > 0)
            {
                parts.Add($"{Count(changes.Added.Count)} added");
            }

            if (changes.Updated.Count > 0)
            {
                parts.Add($"{(parts.Count == 0 ? Count(changes.Updated.Count) : changes.Updated.Count.ToString(CultureInfo.InvariantCulture))} updated");
            }

            if (changes.Removed.Count > 0)
            {
                parts.Add($"{(parts.Count == 0 ? Count(changes.Removed.Count) : changes.Removed.Count.ToString(CultureInfo.InvariantCulture))} removed");
            }

            if (changes.DeletionsChanged)
            {
                parts.Add("forced deletions changed");
            }

            if (parts.Count == 0)
            {
                return "no files";
            }

            long download = changes.Added.Sum(file => file.Size) + changes.Updated.Sum(file => file.Size);
            string text = string.Join(", ", parts);

            return download > 0 ? $"{text} ({FormatSize(download)} to download)" : text;
        }

        private static string Count(int count) =>
            count == 1 ? "1 file" : $"{count.ToString(CultureInfo.InvariantCulture)} files";

        /// <summary>"@Nom" devient une mention si le role est declare, sinon reste du texte.</summary>
        private static string Mention(string message, Dictionary<string, string> roles, List<string> mentioned)
        {
            return Regex.Replace(message, @"@([\p{L}\p{N}_\-]+)", match =>
            {
                if (!roles.TryGetValue(match.Groups[1].Value, out string? id))
                {
                    return match.Value;
                }

                mentioned.Add(id);
                return $"<@&{id}>";
            });
        }

        /// <summary>Tronque sous la limite de Discord ; le detail complet reste dans le fichier joint.</summary>
        private static string Truncate(string content)
        {
            const string more = "\n… (see changes.txt)";

            if (content.Length <= MaxContentLength)
            {
                return content;
            }

            int cut = content.LastIndexOf('\n', MaxContentLength - more.Length);
            return content[..(cut > 0 ? cut : MaxContentLength - more.Length)] + more;
        }

        private static Dictionary<string, string> LoadConfig(string path)
        {
            if (!File.Exists(path))
            {
                throw new ArgumentException($"Configuration introuvable : {path}");
            }

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                int separator = line.IndexOf('=');

                if (line.Length == 0 || line[0] == '#' || separator <= 0)
                {
                    continue;
                }

                values[line[..separator].Trim()] = line[(separator + 1)..].Trim();
            }

            return values;
        }

        private static string FormatSize(long bytes)
        {
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
                : value.ToString("0.#", CultureInfo.InvariantCulture) + " " + units[unit];
        }
    }
}
