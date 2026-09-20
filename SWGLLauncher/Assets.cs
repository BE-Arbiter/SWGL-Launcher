using System.Reflection;

namespace SWGLLauncher
{
    /// <summary>
    /// Acces aux ressources du launcher (fond, musique, visuels Steam). Elles sont
    /// embarquees dans l'executable, mais un fichier pose a cote de l'exe au chemin
    /// indique dans launcher.properties l'emporte : on peut ainsi changer l'habillage
    /// sans recompiler.
    /// </summary>
    internal static class Assets
    {
        private static readonly Assembly Owner = typeof(Assets).Assembly;

        /// <summary>Prefixe des ressources embarquees, issu du dossier "SWGL" du projet.</summary>
        private static readonly string Prefix = $"{Owner.GetName().Name}.SWGL.";

        /// <summary>Ouvre une ressource embarquee, ou null si elle n'existe pas.</summary>
        public static Stream? OpenEmbedded(string fileName)
        {
            return fileName.Length == 0
                ? null
                : Owner.GetManifestResourceStream(Prefix + fileName);
        }

        /// <summary>
        /// Ouvre la ressource designee par une cle de configuration : le fichier sur le
        /// disque s'il existe, sinon la version embarquee de meme nom.
        /// </summary>
        /// <returns>Le flux et l'extension du fichier, ou null si rien n'est disponible.</returns>
        public static (Stream Content, string Extension)? Open(
            LauncherConfig config, string key, string defaultFile)
        {
            string configured = config.GetString(key, defaultFile);

            if (configured.Length == 0)
            {
                return null;
            }

            string extension = Path.GetExtension(configured);

            try
            {
                string path = config.ResolvePath(configured);

                if (File.Exists(path))
                {
                    return (File.OpenRead(path), extension);
                }
            }
            catch
            {
                // Chemin invalide dans la configuration : on retombe sur l'embarque.
            }

            Stream? embedded = OpenEmbedded(Path.GetFileName(configured));
            return embedded is null ? null : (embedded, extension);
        }

        /// <summary>Lit entierement une ressource en memoire.</summary>
        public static byte[]? ReadAllBytes(LauncherConfig config, string key, string defaultFile)
        {
            if (Open(config, key, defaultFile) is not (Stream content, _))
            {
                return null;
            }

            using (content)
            {
                using var buffer = new MemoryStream();
                content.CopyTo(buffer);
                return buffer.ToArray();
            }
        }
    }
}
