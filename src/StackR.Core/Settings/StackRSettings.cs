namespace StackR.Core.Settings;

// Paramètres utilisateur, persistés dans %LOCALAPPDATA%\StackR\stackr.json.
public sealed record StackRSettings
{
    public string Root { get; init; } = @"C:\StackR";

    // Dossier www effectif ; vide = <Root>\www.
    public string WwwPath { get; init; } = "";

    // Composant -> version active (clé : nom du composant en minuscules).
    public Dictionary<string, string> ActiveVersions { get; init; } = new();

    public int HttpPort { get; init; } = 80;

    public int HttpsPort { get; init; } = 443;

    public int MySqlPort { get; init; } = 3306;

    public int MailpitUiPort { get; init; } = 8025;

    public int MailpitSmtpPort { get; init; } = 1025;

    public bool StartWithWindows { get; init; }

    public bool AutoStartServices { get; init; }

    public bool HttpsEnabled { get; init; } = true;

    public string Theme { get; init; } = "System";

    public string Language { get; init; } = "fr";

    public bool FirstRunCompleted { get; init; }

    // Recherche automatique des mises à jour (au lancement puis toutes les 6 h).
    public bool AutoCheckUpdates { get; init; } = true;

    // Dernière version dont l'utilisateur a masqué le bandeau « Mise à jour disponible ».
    public string DismissedUpdateVersion { get; init; } = "";
}
