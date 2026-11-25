using System.Text;

namespace StackR.Core.Hosts;

/// <summary>
/// Lecture/écriture sûre du fichier hosts de Windows (l'écriture exige des droits administrateur
/// sur <see cref="DefaultPath"/> : elle n'est appelée que par <c>stackr hosts sync</c> élevé).
/// </summary>
public static class HostsFile
{
    private const string BackupSuffix = ".stackr.bak";

    // Latin-1 strict : un caractère hors 0x00-0xFF lèverait au lieu d'être remplacé par « ? ».
    private static readonly Encoding StrictLatin1 =
        Encoding.GetEncoding("iso-8859-1", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

    /// <summary>
    /// <c>%SystemRoot%\System32\drivers\etc\hosts</c>. Le dossier Windows est obtenu via l'API
    /// (et non la variable d'environnement, modifiable par un processus non élevé) afin qu'un
    /// processus élevé ne puisse pas être détourné vers un autre fichier.
    /// </summary>
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "drivers", "etc", "hosts");

    /// <summary>Indique si le bloc StackR du fichier diffère de celui attendu pour <paramref name="hosts"/>.</summary>
    /// <exception cref="ArgumentException">Un nom n'est pas un nom <c>.test</c> valide.</exception>
    public static bool NeedsUpdate(IEnumerable<string> hosts, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(hosts);
        var current = Read(path ?? DefaultPath);
        return !string.Equals(HostsBlock.Apply(current.Text, hosts), current.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Met à jour le bloc StackR : sauvegarde <c>hosts.stackr.bak</c> (une seule fois), écriture dans un
    /// fichier temporaire du même dossier puis remplacement, encodage d'origine conservé.
    /// Aucune écriture si le contenu est déjà à jour.
    /// </summary>
    /// <exception cref="ArgumentException">Un nom n'est pas valide (le fichier n'est pas touché).</exception>
    /// <exception cref="InvalidDataException">Encodage non pris en charge (le fichier n'est pas touché).</exception>
    /// <exception cref="UnauthorizedAccessException">Fichier en lecture seule ou droits insuffisants.</exception>
    public static void Write(IEnumerable<string> hosts, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(hosts);
        path = Path.GetFullPath(path ?? DefaultPath);

        var current = Read(path);
        var updated = HostsBlock.Apply(current.Text, hosts); // lève avant toute écriture si un nom est invalide
        if (string.Equals(updated, current.Text, StringComparison.Ordinal))
        {
            return;
        }

        var exists = File.Exists(path);
        if (exists && File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly))
        {
            throw new UnauthorizedAccessException(
                $"Le fichier hosts « {path} » est en lecture seule : StackR ne le modifie pas. Retirez l'attribut lecture seule puis réessayez.");
        }

        var bytes = current.Encode(updated);

        if (exists)
        {
            CreateBackupOnce(path);
        }

        var directory = Path.GetDirectoryName(path)!;
        var temp = Path.Combine(directory, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.stackr.tmp");
        try
        {
            using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes);
                fs.Flush(flushToDisk: true);
            }

            if (exists)
            {
                // ReplaceFile conserve les attributs et la sécurité (ACL) du fichier remplacé.
                File.Replace(temp, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temp, path);
            }
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    private static void CreateBackupOnce(string path)
    {
        var backup = path + BackupSuffix;
        if (File.Exists(backup))
        {
            return;
        }

        try
        {
            File.Copy(path, backup, overwrite: false);
        }
        catch (IOException) when (File.Exists(backup))
        {
            // Créée entre-temps par un autre processus : la première sauvegarde est conservée.
        }
    }

    private static HostsContent Read(string path)
    {
        if (!File.Exists(path))
        {
            return new HostsContent(string.Empty, [], StrictLatin1);
        }

        byte[] bytes;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var ms = new MemoryStream())
        {
            fs.CopyTo(ms);
            bytes = ms.ToArray();
        }

        return HostsContent.Decode(bytes);
    }

    /// <summary>
    /// Contenu décodé + moyen de le ré-encoder à l'identique.
    /// Sans BOM UTF-16, le fichier (ANSI, ASCII ou UTF-8) est lu en Latin-1 : correspondance
    /// octet ↔ caractère bijective, donc les octets étrangers sont restitués exactement ;
    /// StackR n'ajoute que de l'ASCII, valide dans tous ces encodages.
    /// </summary>
    private sealed record HostsContent(string Text, byte[] Preamble, Encoding Encoding)
    {
        public static HostsContent Decode(byte[] bytes)
        {
            ReadOnlySpan<byte> span = bytes;

            if (span.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE, 0x00, 0x00]) || span.StartsWith((ReadOnlySpan<byte>)[0x00, 0x00, 0xFE, 0xFF]))
            {
                throw new InvalidDataException("Fichier hosts encodé en UTF-32 : encodage non pris en charge, aucune modification effectuée.");
            }

            if (span.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
            {
                return DecodeUtf16(bytes, bigEndian: false);
            }

            if (span.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
            {
                return DecodeUtf16(bytes, bigEndian: true);
            }

            byte[] preamble = span.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? [0xEF, 0xBB, 0xBF] : [];
            return new HostsContent(StrictLatin1.GetString(bytes, preamble.Length, bytes.Length - preamble.Length), preamble, StrictLatin1);
        }

        private static HostsContent DecodeUtf16(byte[] bytes, bool bigEndian)
        {
            // Décodage strict : un contenu UTF-16 invalide lève au lieu d'être altéré par des caractères de remplacement.
            var encoding = new UnicodeEncoding(bigEndian, byteOrderMark: false, throwOnInvalidBytes: true);
            try
            {
                return new HostsContent(encoding.GetString(bytes, 2, bytes.Length - 2), bytes[..2], encoding);
            }
            catch (DecoderFallbackException ex)
            {
                throw new InvalidDataException("Fichier hosts UTF-16 invalide : aucune modification effectuée.", ex);
            }
        }

        public byte[] Encode(string text)
        {
            try
            {
                return [.. Preamble, .. Encoding.GetBytes(text)];
            }
            catch (EncoderFallbackException ex)
            {
                throw new InvalidDataException("Impossible de ré-encoder le fichier hosts à l'identique : aucune modification effectuée.", ex);
            }
        }
    }
}
