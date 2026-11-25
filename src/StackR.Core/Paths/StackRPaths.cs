namespace StackR.Core.Paths;

// Arborescence d'installation de StackR (par défaut C:\StackR, voir design §5.1).
public sealed record StackRPaths(string Root)
{
    public string Bin => System.IO.Path.Combine(Root, "bin");

    public string Shims => System.IO.Path.Combine(Bin, "shims");

    public string Etc => System.IO.Path.Combine(Root, "etc");

    public string Data => System.IO.Path.Combine(Root, "data");

    public string MySqlData => System.IO.Path.Combine(Data, "mysql");

    public string Ssl => System.IO.Path.Combine(Data, "ssl");

    public string Secrets => System.IO.Path.Combine(Data, "secrets");

    public string Www => System.IO.Path.Combine(Root, "www");

    public string Logs => System.IO.Path.Combine(Root, "logs");

    public string Tmp => System.IO.Path.Combine(Root, "tmp");

    public string StateFile => System.IO.Path.Combine(Tmp, "state.json");

    // Dossier d'un composant installé : bin\<component>\<version>.
    public string ComponentDir(string component, string version) =>
        System.IO.Path.Combine(Bin, component, version);

    // Crée l'arborescence si elle n'existe pas encore (idempotent).
    public void EnsureCreated()
    {
        foreach (var dir in new[] { Bin, Shims, Etc, MySqlData, Ssl, Secrets, Www, Logs, Tmp })
        {
            Directory.CreateDirectory(dir);
        }
    }
}
