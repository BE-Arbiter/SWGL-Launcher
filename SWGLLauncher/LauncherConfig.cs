using System.Globalization;

namespace SWGLLauncher
{
    /// <summary>
    /// Lecture du fichier "launcher.properties" (format cle=valeur).
    /// Toute valeur absente retombe sur la valeur par defaut fournie a l'appel.
    /// </summary>
    internal sealed class LauncherConfig
    {
        public const string DefaultFileName = "launcher.properties";

        private readonly Dictionary<string, string> _values =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Dossier de reference pour resoudre les chemins relatifs.</summary>
        public string BaseDirectory { get; }

        /// <summary>Chemin du fichier de configuration lu (meme s'il n'existe pas).</summary>
        public string FilePath { get; }

        private LauncherConfig(string filePath, string baseDirectory)
        {
            FilePath = filePath;
            BaseDirectory = baseDirectory;
        }

        /// <summary>
        /// Charge le fichier de configuration place a cote de l'executable.
        /// Si le fichier est absent, la configuration par defaut est utilisee.
        /// </summary>
        public static LauncherConfig Load(string? fileName = null)
        {
            string baseDirectory = AppContext.BaseDirectory;
            string filePath = Path.Combine(baseDirectory, fileName ?? DefaultFileName);
            var config = new LauncherConfig(filePath, baseDirectory);

            if (!File.Exists(filePath))
            {
                return config;
            }

            foreach (string rawLine in File.ReadAllLines(filePath))
            {
                string line = rawLine.Trim();

                // Commentaires et lignes vides
                if (line.Length == 0 || line[0] == '#' || line[0] == '!')
                {
                    continue;
                }

                int separator = line.IndexOfAny(['=', ':']);
                if (separator <= 0)
                {
                    continue;
                }

                string key = line[..separator].Trim();
                string value = line[(separator + 1)..].Trim();
                config._values[key] = value;
            }

            return config;
        }

        public string GetString(string key, string defaultValue)
        {
            return _values.TryGetValue(key, out string? value) && value.Length > 0
                ? value
                : defaultValue;
        }

        /// <summary>
        /// Enregistre une valeur dans le fichier de configuration en remplacant la ligne
        /// existante (les commentaires et l'ordre du fichier sont preserves) ou en
        /// l'ajoutant a la fin. Utilise pour les reglages modifiables depuis l'interface.
        /// </summary>
        public void Set(string key, string value)
        {
            _values[key] = value;

            try
            {
                List<string> lines = File.Exists(FilePath)
                    ? [.. File.ReadAllLines(FilePath)]
                    : [];

                bool replaced = false;

                for (int i = 0; i < lines.Count; i++)
                {
                    string line = lines[i].Trim();

                    if (line.Length == 0 || line[0] == '#' || line[0] == '!')
                    {
                        continue;
                    }

                    int separator = line.IndexOfAny(['=', ':']);
                    if (separator <= 0 || !line[..separator].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    lines[i] = $"{key}={value}";
                    replaced = true;
                    break;
                }

                if (!replaced)
                {
                    lines.Add($"{key}={value}");
                }

                File.WriteAllLines(FilePath, lines);
            }
            catch
            {
                // Fichier en lecture seule ou dossier protege : le reglage reste actif
                // pour la session en cours, il ne sera simplement pas memorise.
            }
        }

        public int GetInt(string key, int defaultValue)
        {
            return _values.TryGetValue(key, out string? value)
                && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : defaultValue;
        }

        public bool GetBool(string key, bool defaultValue)
        {
            if (!_values.TryGetValue(key, out string? value))
            {
                return defaultValue;
            }

            return value.ToLowerInvariant() switch
            {
                "true" or "1" or "yes" or "oui" or "on" => true,
                "false" or "0" or "no" or "non" or "off" => false,
                _ => defaultValue,
            };
        }

        /// <summary>Couleur au format #RGB, #RRGGBB, #AARRGGBB ou nom .NET ("Red", "Transparent"...).</summary>
        public Color GetColor(string key, Color defaultValue)
        {
            if (!_values.TryGetValue(key, out string? value) || value.Length == 0)
            {
                return defaultValue;
            }

            value = value.Trim();

            if (value[0] == '#')
            {
                string hex = value[1..];

                // Forme courte #RGB -> #RRGGBB
                if (hex.Length == 3)
                {
                    hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);
                }

                if ((hex.Length == 6 || hex.Length == 8)
                    && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint argb))
                {
                    if (hex.Length == 6)
                    {
                        argb |= 0xFF000000;
                    }

                    return Color.FromArgb(unchecked((int)argb));
                }

                return defaultValue;
            }

            try
            {
                Color named = Color.FromName(value);
                return named.IsKnownColor ? named : defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// Resout un chemin : absolu tel quel, relatif depuis le dossier de l.executable.
        /// </summary>
        public string ResolvePath(string value)
        {
            return Path.IsPathRooted(value)
                ? Path.GetFullPath(value)
                : Path.GetFullPath(Path.Combine(BaseDirectory, value));
        }

        /// <summary>
        /// Resout un chemin de la configuration : absolu tel quel, relatif depuis le dossier
        /// de l'executable. Retourne null si la valeur est vide ou si le fichier est introuvable.
        /// </summary>
        public string? GetExistingPath(string key, string defaultValue)
        {
            string value = GetString(key, defaultValue);
            if (value.Length == 0)
            {
                return null;
            }

            string path = ResolvePath(value);

            return File.Exists(path) ? path : null;
        }
    }
}
