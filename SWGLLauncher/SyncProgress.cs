namespace SWGLLauncher
{
    /// <summary>Etat d'avancement d'une operation longue, rapporte a l'interface.</summary>
    /// <param name="Status">Ligne principale ("Downloading 3/12...").</param>
    /// <param name="Detail">Ligne secondaire (nom du fichier, vitesse).</param>
    /// <param name="Fraction">Avancement de 0 a 1, ou -1 si indetermine.</param>
    internal sealed record SyncProgress(string Status, string Detail = "", double Fraction = -1);
}
