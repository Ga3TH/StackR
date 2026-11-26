namespace StackR.Core.Tests;

// Dossier temporaire unique, supprimé à la fin du test.
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stackr-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        // Des écritures asynchrones (événements d'arrêt de processus) peuvent encore tenir un fichier : on réessaie.
        for (var attempt = 1; Directory.Exists(Path); attempt++)
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException) when (attempt < 10)
            {
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException) when (attempt < 10)
            {
                Thread.Sleep(100);
            }
        }
    }
}
