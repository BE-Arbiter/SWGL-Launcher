namespace SWGLLauncher
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            // Lance par une ancienne version pour la remplacer : rien d'autre a faire.
            if (SelfUpdater.TryApplyUpdate(args))
            {
                return;
            }

            ApplicationConfiguration.Initialize();
            Application.Run(new LauncherForm());
        }
    }
}
