namespace StackR.Core.Sites;

// Site détecté dans le dossier www.
public sealed record Site(SiteName Name, string FolderPath, string DocumentRoot);
