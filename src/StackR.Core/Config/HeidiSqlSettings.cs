using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using StackR.Core.IO;

namespace StackR.Core.Config;

/// <summary>
/// Session « StackR » dans les réglages de HeidiSQL portable. Depuis HeidiSQL 12.x la présence de
/// <c>portable.lock</c> à côté de <c>heidisql.exe</c> active le mode portable, et les réglages sont dans
/// <c>settings.json</c> au même endroit (clé <c>Servers/&lt;session&gt;</c>). Le mot de passe y est stocké avec
/// l'encodage réversible propre à HeidiSQL (voir <see cref="EncodePassword"/>) : jamais en ligne de commande.
/// </summary>
public static class HeidiSqlSettings
{
    public const string SessionName = "StackR";
    public const string SettingsFileName = "settings.json";
    public const string PortableLockFileName = "portable.lock";

    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    /// <summary>
    /// Encode un mot de passe comme la fonction <c>encrypt</c> de HeidiSQL : chaque caractère + sel (1 à 9), en hexadécimal
    /// (2 chiffres, modulo 255 ; 4 chiffres et suffixe « 0 » si un caractère dépasse 255), puis le sel en dernier chiffre.
    /// Ce n'est pas du chiffrement : c'est le format que HeidiSQL relit, rien de plus.
    /// </summary>
    public static string EncodePassword(string password, int salt)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentOutOfRangeException.ThrowIfLessThan(salt, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(salt, 9);
        if (password.Length == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        if (password.Any(c => c > 255))
        {
            foreach (var c in password)
            {
                sb.Append(((c + salt) % 65536).ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
            }

            return sb.Append(salt).Append('0').ToString();
        }

        foreach (var c in password)
        {
            var nr = c + salt;
            if (nr > 255)
            {
                nr -= 255;
            }

            sb.Append(nr.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        }

        return sb.Append(salt).ToString();
    }

    /// <summary>
    /// Crée ou met à jour la session « StackR » (127.0.0.1, <paramref name="port"/>, root) dans <c>settings.json</c>
    /// de <paramref name="heidiDir"/>, en conservant les autres réglages. Mot de passe vide si <paramref name="password"/> est null.
    /// </summary>
    /// <exception cref="InvalidDataException">Le fichier existe mais n'est pas un objet JSON (il n'est pas modifié).</exception>
    public static void WriteSession(string heidiDir, int port, string? password, int? salt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(heidiDir);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);

        var root = Load(heidiDir);
        var servers = Child(root, "Servers");
        var session = Child(servers, SessionName);
        session["Host"] = "127.0.0.1";
        session["Port"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture); // HeidiSQL lit le port comme texte
        session["User"] = "root";
        session["Password"] = string.IsNullOrEmpty(password)
            ? string.Empty
            : EncodePassword(password, salt ?? RandomNumberGenerator.GetInt32(1, 10));
        session["NetType"] = 0; // MySQL (TCP/IP)
        session["LoginPrompt"] = false;
        session["WindowsAuth"] = false;

        Save(heidiDir, root);
    }

    /// <summary>Vide le mot de passe de la session (au mieux : sans effet si le fichier est absent, illisible ou verrouillé).</summary>
    public static void ClearPassword(string heidiDir)
    {
        try
        {
            var root = Load(heidiDir);
            if (root["Servers"] is JsonObject servers && servers[SessionName] is JsonObject session && session.ContainsKey("Password"))
            {
                session["Password"] = string.Empty;
                Save(heidiDir, root);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // Au mieux.
        }
    }

    private static JsonObject Load(string heidiDir)
    {
        var path = Path.Combine(heidiDir, SettingsFileName);
        if (!File.Exists(path))
        {
            return [];
        }

        var text = File.ReadAllText(path, Encoding.UTF8);
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        try
        {
            return JsonNode.Parse(text) as JsonObject
                ?? throw new InvalidDataException($"« {path} » n'est pas un objet JSON : réglages HeidiSQL non modifiés.");
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"« {path} » est illisible ({e.Message}) : réglages HeidiSQL non modifiés.", e);
        }
    }

    private static void Save(string heidiDir, JsonObject root)
    {
        AtomicFile.WriteAllText(Path.Combine(heidiDir, SettingsFileName), root.ToJsonString(Pretty), new UTF8Encoding(false));
        var lockFile = Path.Combine(heidiDir, PortableLockFileName);
        if (!File.Exists(lockFile))
        {
            File.WriteAllBytes(lockFile, []);
        }
    }

    private static JsonObject Child(JsonObject parent, string name)
    {
        if (parent[name] is JsonObject existing)
        {
            return existing;
        }

        var created = new JsonObject();
        parent[name] = created;
        return created;
    }
}
