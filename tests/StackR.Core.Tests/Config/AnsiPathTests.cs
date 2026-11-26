using StackR.Core.Config;
using StackR.Core.Paths;
using StackR.Core.Settings;

namespace StackR.Core.Tests.Config;

public sealed class AnsiPathTests
{
    [Fact]
    public void For_AsciiPath_IsUnchanged()
    {
        Assert.Equal(@"C:\StackR\bin\mysql", AnsiPath.For(@"C:\StackR\bin\mysql"));
    }

    [Fact]
    public void For_UnrepresentableExistingFolder_ReturnsRepresentablePathToSameFolder()
    {
        using var temp = new TempDirectory();
        // Caractères absents de toute page de code ANSI occidentale.
        var dir = Path.Combine(temp.Path, "projet-日本-Ω", "data");
        Directory.CreateDirectory(dir);

        string result;
        try
        {
            result = AnsiPath.For(dir);
        }
        catch (InvalidOperationException)
        {
            // Noms courts 8.3 désactivés sur ce volume : l'erreur claire est le comportement attendu.
            return;
        }

        Assert.True(AnsiPath.IsRepresentable(result));
        Assert.True(Directory.Exists(result));
        Assert.Equal(
            Path.GetFullPath(dir).TrimEnd('\\'),
            new DirectoryInfo(result).FullName.TrimEnd('\\'),
            ignoreCase: true);
    }

    [Fact]
    public void WriteMyIni_AccentedRoot_IsWrittenInAnsiCodePage()
    {
        using var temp = new TempDirectory();
        var paths = new StackRPaths(Path.Combine(temp.Path, "Dossier é", "StackR"));
        var writer = new ConfigWriter(paths, new StackRSettings { Root = paths.Root }, () => []);

        var file = writer.WriteMyIni(Path.Combine(paths.Root, "bin", "mysql", "8.4.13"));

        var text = File.ReadAllText(file, AnsiPath.Encoding);
        Assert.Contains("Dossier é/StackR/data/mysql", text, StringComparison.Ordinal);
        Assert.Equal(text, AnsiPath.Encoding.GetString(File.ReadAllBytes(file)));
    }
}
