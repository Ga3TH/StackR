using System.CommandLine;
using System.Security.Cryptography;

namespace StackR.Cli.Commands;

/// <summary><c>db password [--copy]</c> et <c>ca trust</c>.</summary>
internal static class SecurityCommands
{
    public static Command Database(CliContext ctx)
    {
        var copy = new Option<bool>("--copy") { Description = "Copie le mot de passe dans le presse-papiers au lieu de l'afficher." };
        var password = new Command("password", "Affiche le mot de passe root de MySQL (stocké chiffré par DPAPI).") { copy };
        password.SetAction(CliApp.Guarded(ctx, (pr, _) => PasswordAsync(ctx, pr.GetValue(copy))));
        return new Command("db", "Base de données MySQL.") { password };
    }

    public static Command CertificateAuthority(CliContext ctx)
    {
        var trust = new Command("trust", "Ajoute la CA locale de StackR aux autorités approuvées de l'utilisateur (confirmation Windows).");
        trust.SetAction(CliApp.Guarded(ctx, (_, _) => Task.FromResult(Trust(ctx))));
        return new Command("ca", "Autorité de certification locale (HTTPS des sites .test).") { trust };
    }

    private static async Task<int> PasswordAsync(CliContext ctx, bool copy)
    {
        var secret = ctx.Engine.GetMySqlRootPassword();
        if (secret is null)
        {
            ctx.Out.WriteLine("Mot de passe root introuvable : MySQL n'a pas encore été initialisé (lancez « stackr start mysql »).");
            return ExitCodes.Error;
        }

        if (!copy)
        {
            ctx.Out.WriteLine(secret);
            return ExitCodes.Ok;
        }

        if (!await ctx.CopyToClipboard(secret).ConfigureAwait(false))
        {
            ctx.Out.WriteLine("Impossible de copier le mot de passe dans le presse-papiers.");
            return ExitCodes.Error;
        }

        ctx.Out.WriteLine("Mot de passe root de MySQL copié dans le presse-papiers.");
        return ExitCodes.Ok;
    }

    private static int Trust(CliContext ctx)
    {
        try
        {
            ctx.Engine.TrustCertificateAuthority();
        }
        catch (CryptographicException ex)
        {
            ctx.Out.WriteLine($"La CA n'a pas été approuvée (ajout refusé ou impossible) : {ex.Message}");
            return ExitCodes.Error;
        }

        ctx.Out.WriteLine("CA locale de StackR approuvée : les sites .test en HTTPS ne déclenchent plus d'alerte (redémarrez le navigateur si besoin).");
        return ExitCodes.Ok;
    }
}
