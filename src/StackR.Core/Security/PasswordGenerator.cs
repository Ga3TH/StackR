using System.Security.Cryptography;

namespace StackR.Core.Security;

/// <summary>Génère des mots de passe sûrs pour .env et my.ini.</summary>
public static class PasswordGenerator
{
    // Pas de caractères qui cassent .env ou my.ini (guillemets, #, =, espaces, etc.).
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_.";

    public static string Generate(int length = 24)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        return RandomNumberGenerator.GetString(Alphabet, length);
    }
}
