using System.Diagnostics;
using System.Text;
using Microsoft.Win32;

namespace SWGLLauncher
{
    /// <summary>
    /// Ajout de SWGL aux jeux non-Steam, en ecrivant dans "shortcuts.vdf"
    /// (format VDF binaire du profil Steam).
    /// </summary>
    internal static class SteamShortcutsService
    {
        /// <summary>Resultat d'une ecriture dans shortcuts.vdf.</summary>
        /// <param name="Created">Vrai si un raccourci a ete cree, faux s'il a ete mis a jour.</param>
        /// <param name="File">Fichier modifie.</param>
        /// <param name="Backup">Copie de sauvegarde, si un fichier existait deja.</param>
        /// <param name="AppId">Identifiant du raccourci, qui sert a nommer les visuels.</param>
        internal sealed record Result(bool Created, string File, string? Backup, uint AppId);

        /// <summary>Visuel de la fiche Steam et suffixe de fichier attendu par Steam.</summary>
        internal enum SteamArtwork
        {
            /// <summary>Capsule verticale de la bibliotheque, "&lt;appid&gt;p".</summary>
            Cover,

            /// <summary>Capsule large, "&lt;appid&gt;".</summary>
            WideCover,

            /// <summary>Logo transparent superpose, "&lt;appid&gt;_logo".</summary>
            Logo,

            /// <summary>Banniere de la page du jeu, "&lt;appid&gt;_hero".</summary>
            Hero,
        }

        /// <summary>Vrai si Steam tourne : il reecrirait le fichier en se fermant.</summary>
        public static bool IsSteamRunning() => Process.GetProcessesByName("steam").Length > 0;

        /// <summary>
        /// Localise le "shortcuts.vdf" du profil Steam le plus recemment utilise.
        /// Le fichier n'existe pas forcement : tant qu'aucun jeu non-Steam n'a ete ajoute,
        /// c'est normal, et le chemin retourne est celui a creer.
        /// </summary>
        public static string? FindShortcutsFile(string? explicitFile, string? steamExecutable)
        {
            if (!string.IsNullOrWhiteSpace(explicitFile))
            {
                return explicitFile;
            }

            string? steamRoot = ResolveSteamRoot(steamExecutable);
            if (steamRoot is null)
            {
                return null;
            }

            string userData = Path.Combine(steamRoot, "userdata");
            if (!Directory.Exists(userData))
            {
                return null;
            }

            // Un dossier par compte : on prend celui dont la configuration est la plus recente.
            string? best = null;
            DateTime bestDate = DateTime.MinValue;

            foreach (string accountFolder in Directory.GetDirectories(userData))
            {
                string candidate = Path.Combine(accountFolder, "config", "shortcuts.vdf");
                DateTime date = File.Exists(candidate)
                    ? File.GetLastWriteTimeUtc(candidate)
                    : Directory.GetLastWriteTimeUtc(accountFolder);

                if (date > bestDate)
                {
                    bestDate = date;
                    best = candidate;
                }
            }

            return best;
        }

        private static string? ResolveSteamRoot(string? steamExecutable)
        {
            if (!string.IsNullOrWhiteSpace(steamExecutable) && File.Exists(steamExecutable))
            {
                return Path.GetDirectoryName(steamExecutable);
            }

            foreach ((RegistryKey root, string subKey, string name) in new[]
            {
                (Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
                (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
                (Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"),
            })
            {
                using RegistryKey? key = root.OpenSubKey(subKey);
                if (key?.GetValue(name) is string value && Directory.Exists(value))
                {
                    return value.Replace('/', '\\');
                }
            }

            string fallback = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");

            return Directory.Exists(fallback) ? fallback : null;
        }

        /// <summary>
        /// Ajoute le raccourci, ou met a jour celui qui pointe deja sur le meme executable.
        /// Le fichier existant est sauvegarde avant ecriture.
        /// </summary>
        public static Result AddOrUpdate(
            string shortcutsFile,
            string appName,
            string executablePath,
            string? startDirectory = null,
            string launchOptions = "")
        {
            // Steam stocke l'executable entre guillemets et le dossier avec un antislash final.
            string exe = $"\"{executablePath}\"";
            string startDir = startDirectory
                ?? Path.GetDirectoryName(executablePath)
                ?? string.Empty;

            if (startDir.Length > 0 && !startDir.EndsWith('\\'))
            {
                startDir += '\\';
            }

            VdfMap root = File.Exists(shortcutsFile)
                ? ReadDocument(shortcutsFile)
                : CreateEmptyDocument();

            VdfMap shortcuts = root.GetMap("shortcuts") ?? throw new InvalidDataException(
                "shortcuts.vdf does not contain a \"shortcuts\" section.");

            VdfMap? existing = null;

            foreach ((string _, object value) in shortcuts.Items)
            {
                if (value is VdfMap entry
                    && entry.GetString("Exe") is string entryExe
                    && entryExe.Trim('"').Equals(executablePath, StringComparison.OrdinalIgnoreCase))
                {
                    existing = entry;
                    break;
                }
            }

            bool created = existing is null;
            VdfMap shortcut = existing ?? CreateShortcut();

            uint appId = Crc32(exe + appName) | 0x80000000;
            shortcut.Set("appid", unchecked((int)appId));
            shortcut.Set("AppName", appName);
            shortcut.Set("Exe", exe);
            shortcut.Set("StartDir", startDir);
            shortcut.Set("LaunchOptions", launchOptions);

            // Steam prend l'icone dans l'executable indique.
            shortcut.Set("icon", executablePath);

            if (created)
            {
                shortcuts.Items.Add(new KeyValuePair<string, object>(
                    shortcuts.Items.Count.ToString(), shortcut));
            }

            string? backup = null;

            if (File.Exists(shortcutsFile))
            {
                backup = $"{shortcutsFile}.bak-{DateTime.Now:yyyyMMdd-HHmmss}";
                File.Copy(shortcutsFile, backup, overwrite: true);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(shortcutsFile)!);
            }

            WriteDocument(shortcutsFile, root);
            return new Result(created, shortcutsFile, backup, appId);
        }

        /// <summary>
        /// Depose un visuel dans le dossier "grid" du profil, sous le nom attendu par Steam.
        /// L'extension d'origine est conservee ; les variantes d'autres extensions portant le
        /// meme nom sont retirees pour que Steam n'ait pas deux candidats.
        /// </summary>
        /// <returns>Le fichier ecrit.</returns>
        public static string InstallArtwork(
            string shortcutsFile, uint appId, SteamArtwork kind, Stream source, string extension)
        {
            string grid = Path.Combine(
                Path.GetDirectoryName(shortcutsFile) ?? string.Empty, "grid");

            Directory.CreateDirectory(grid);

            string baseName = appId + kind switch
            {
                SteamArtwork.Cover => "p",
                SteamArtwork.Logo => "_logo",
                SteamArtwork.Hero => "_hero",
                _ => string.Empty,
            };

            string destination = Path.Combine(grid, baseName + extension);

            foreach (string existing in Directory.GetFiles(grid, baseName + ".*"))
            {
                if (!existing.Equals(destination, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(existing);
                }
            }

            using (FileStream file = File.Create(destination))
            {
                source.CopyTo(file);
            }

            return destination;
        }

        /// <summary>Raccourci neuf, avec les memes champs que ceux ecrits par Steam.</summary>
        private static VdfMap CreateShortcut()
        {
            var shortcut = new VdfMap();

            shortcut.Set("appid", 0);
            shortcut.Set("AppName", string.Empty);
            shortcut.Set("Exe", string.Empty);
            shortcut.Set("StartDir", string.Empty);
            shortcut.Set("icon", string.Empty);
            shortcut.Set("ShortcutPath", string.Empty);
            shortcut.Set("LaunchOptions", string.Empty);
            shortcut.Set("IsHidden", 0);
            shortcut.Set("AllowDesktopConfig", 1);
            shortcut.Set("AllowOverlay", 1);
            shortcut.Set("OpenVR", 0);
            shortcut.Set("Devkit", 0);
            shortcut.Set("DevkitGameID", string.Empty);
            shortcut.Set("DevkitOverrideAppID", 0);
            shortcut.Set("LastPlayTime", 0);
            shortcut.Set("FlatpakAppID", string.Empty);
            shortcut.Items.Add(new KeyValuePair<string, object>("tags", new VdfMap()));

            return shortcut;
        }

        private static VdfMap CreateEmptyDocument()
        {
            var root = new VdfMap();
            root.Items.Add(new KeyValuePair<string, object>("shortcuts", new VdfMap()));
            return root;
        }

        // ------------------------------------------------------------------
        // VDF binaire
        // ------------------------------------------------------------------

        private const byte TypeMap = 0x00;
        private const byte TypeString = 0x01;
        private const byte TypeInt32 = 0x02;
        private const byte TypeEnd = 0x08;

        /// <summary>Noeud du document : suite ordonnee de cles et de valeurs.</summary>
        internal sealed class VdfMap
        {
            public List<KeyValuePair<string, object>> Items { get; } = [];

            public VdfMap? GetMap(string key) => Find(key) as VdfMap;

            public string? GetString(string key) => Find(key) as string;

            private object? Find(string key)
            {
                foreach ((string itemKey, object value) in Items)
                {
                    if (itemKey.Equals(key, StringComparison.OrdinalIgnoreCase))
                    {
                        return value;
                    }
                }

                return null;
            }

            /// <summary>Remplace la valeur si la cle existe, sinon l'ajoute a la fin.</summary>
            public void Set(string key, object value)
            {
                for (int i = 0; i < Items.Count; i++)
                {
                    if (Items[i].Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                    {
                        Items[i] = new KeyValuePair<string, object>(Items[i].Key, value);
                        return;
                    }
                }

                Items.Add(new KeyValuePair<string, object>(key, value));
            }
        }

        private static VdfMap ReadDocument(string path)
        {
            using FileStream stream = File.OpenRead(path);
            return ReadMap(stream);
        }

        private static VdfMap ReadMap(Stream stream)
        {
            var map = new VdfMap();

            while (true)
            {
                int type = stream.ReadByte();

                if (type == TypeEnd || type < 0)
                {
                    return map;
                }

                string key = ReadString(stream);

                object value = type switch
                {
                    TypeMap => ReadMap(stream),
                    TypeString => ReadString(stream),
                    TypeInt32 => ReadInt32(stream),
                    _ => throw new InvalidDataException($"Unknown VDF entry type 0x{type:X2}."),
                };

                map.Items.Add(new KeyValuePair<string, object>(key, value));
            }
        }

        private static string ReadString(Stream stream)
        {
            var bytes = new List<byte>(64);

            while (true)
            {
                int read = stream.ReadByte();

                if (read <= 0)
                {
                    break;
                }

                bytes.Add((byte)read);
            }

            return Encoding.UTF8.GetString([.. bytes]);
        }

        private static int ReadInt32(Stream stream)
        {
            Span<byte> buffer = stackalloc byte[4];
            stream.ReadExactly(buffer);
            return BitConverter.ToInt32(buffer);
        }

        private static void WriteDocument(string path, VdfMap root)
        {
            using var buffer = new MemoryStream();

            WriteMapContents(buffer, root);
            buffer.WriteByte(TypeEnd);

            // Ecriture en deux temps : le fichier de Steam n'est jamais laisse a moitie ecrit.
            string temporary = path + ".tmp";
            File.WriteAllBytes(temporary, buffer.ToArray());
            File.Move(temporary, path, overwrite: true);
        }

        private static void WriteMapContents(Stream stream, VdfMap map)
        {
            foreach ((string key, object value) in map.Items)
            {
                switch (value)
                {
                    case VdfMap nested:
                        stream.WriteByte(TypeMap);
                        WriteString(stream, key);
                        WriteMapContents(stream, nested);
                        stream.WriteByte(TypeEnd);
                        break;

                    case int number:
                        stream.WriteByte(TypeInt32);
                        WriteString(stream, key);
                        stream.Write(BitConverter.GetBytes(number));
                        break;

                    default:
                        stream.WriteByte(TypeString);
                        WriteString(stream, key);
                        WriteString(stream, value?.ToString() ?? string.Empty);
                        break;
                }
            }
        }

        private static void WriteString(Stream stream, string value)
        {
            stream.Write(Encoding.UTF8.GetBytes(value));
            stream.WriteByte(0);
        }

        /// <summary>
        /// CRC32 (IEEE) utilise par Steam pour deriver l'identifiant d'un jeu non-Steam.
        /// </summary>
        private static uint Crc32(string value)
        {
            uint[] table = new uint[256];

            for (uint i = 0; i < 256; i++)
            {
                uint entry = i;

                for (int bit = 0; bit < 8; bit++)
                {
                    entry = (entry & 1) != 0 ? 0xEDB88320u ^ (entry >> 1) : entry >> 1;
                }

                table[i] = entry;
            }

            uint crc = 0xFFFFFFFFu;

            foreach (byte b in Encoding.UTF8.GetBytes(value))
            {
                crc = table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            }

            return crc ^ 0xFFFFFFFFu;
        }
    }
}
