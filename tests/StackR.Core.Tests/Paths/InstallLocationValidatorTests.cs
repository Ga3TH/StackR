using StackR.Core.Paths;

namespace StackR.Core.Tests.Paths;

public sealed class InstallLocationValidatorTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    // Règles de test : dossiers « protégés » et « Laragon » simulés dans le dossier temporaire.
    private LocationRules Rules => new(
        [Path.Combine(_temp.Path, "Program Files"), Path.Combine(_temp.Path, "Windows")],
        Path.Combine(_temp.Path, "laragon"));

    [Theory]
    [InlineData(null, LocationProblem.Empty)]
    [InlineData("   ", LocationProblem.Empty)]
    [InlineData(@"StackR", LocationProblem.NotAbsolute)]
    [InlineData(@"\StackR", LocationProblem.NotAbsolute)]
    [InlineData(@"C:StackR", LocationProblem.NotAbsolute)]
    [InlineData(@"\\serveur\partage\StackR", LocationProblem.NetworkPath)]
    [InlineData(@"C:\Stack*R", LocationProblem.InvalidCharacters)]
    [InlineData(@"C:\StackR\flux:secret", LocationProblem.InvalidCharacters)]
    [InlineData(@"C:\StackR \www", LocationProblem.InvalidCharacters)]
    [InlineData(@"C:\StackR.", LocationProblem.InvalidCharacters)]
    [InlineData(@"C:\", LocationProblem.DriveRoot)]
    [InlineData(@"D:/", LocationProblem.DriveRoot)]
    public void Normalize_InvalidInput_ReturnsProblem(string? input, LocationProblem expected)
    {
        Assert.Equal(expected, InstallLocationValidator.Normalize(input).Problem);
    }

    [Theory]
    [InlineData(@"C:\StackR", @"C:\StackR")]
    [InlineData(@"  ""C:\Mes projets\Développement é""  ", @"C:\Mes projets\Développement é")]
    [InlineData(@"c:/StackR/", @"c:\StackR")]
    [InlineData(@"C:\StackR\..\Autre\", @"C:\Autre")]
    public void Normalize_ValidInput_ReturnsFullPath(string input, string expected)
    {
        var check = InstallLocationValidator.Normalize(input);

        Assert.True(check.IsValid);
        Assert.Equal(expected, check.FullPath);
    }

    [Fact]
    public void ValidateRoot_UnderProgramFiles_IsRefused()
    {
        var check = InstallLocationValidator.ValidateRoot(Path.Combine(_temp.Path, "program files", "StackR"), Rules, checkAccess: false);

        Assert.Equal(LocationProblem.ProtectedFolder, check.Problem);
    }

    [Fact]
    public void ValidateRoot_DefaultRules_RefuseRealProgramFiles()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        var check = InstallLocationValidator.ValidateRoot(Path.Combine(programFiles, "StackR"), LocationRules.Default, checkAccess: false);

        Assert.Equal(LocationProblem.ProtectedFolder, check.Problem);
    }

    [Fact]
    public void ValidateRoot_InsideLaragon_IsRefused()
    {
        var check = InstallLocationValidator.ValidateRoot(Path.Combine(_temp.Path, "Laragon", "StackR"), Rules, checkAccess: false);

        Assert.Equal(LocationProblem.LaragonFolder, check.Problem);
    }

    [Fact]
    public void ValidateRoot_ExistingFile_IsRefused()
    {
        var file = Path.Combine(_temp.Path, "fichier");
        File.WriteAllText(file, "x");

        Assert.Equal(LocationProblem.IsFile, InstallLocationValidator.ValidateRoot(file, Rules, checkAccess: true).Problem);
    }

    [Fact]
    public void ValidateRoot_MissingFolder_IsWritable_AndLeavesNoTrace()
    {
        var root = Path.Combine(_temp.Path, "Mes outils é", "StackR");

        var check = InstallLocationValidator.ValidateRoot(root, Rules, checkAccess: true);

        Assert.True(check.IsValid, check.Detail);
        Assert.Equal(root, check.FullPath);
        Assert.False(Directory.Exists(Path.Combine(_temp.Path, "Mes outils é")));
    }

    [Fact]
    public void ValidateRoot_ExistingFolder_KeepsContent_AndLeavesNoProbeFile()
    {
        var root = Path.Combine(_temp.Path, "StackR");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "garder.txt"), "x");

        var check = InstallLocationValidator.ValidateRoot(root, Rules, checkAccess: true);

        Assert.True(check.IsValid, check.Detail);
        Assert.Equal(["garder.txt"], Directory.GetFileSystemEntries(root).Select(e => Path.GetFileName(e)).ToArray());
    }

    [Fact]
    public void ValidateWww_LaragonWww_IsAllowed_WithoutWriting()
    {
        var root = Path.Combine(_temp.Path, "StackR");
        var www = Path.Combine(_temp.Path, "laragon", "www");
        Directory.CreateDirectory(Path.Combine(www, "blog"));

        var check = InstallLocationValidator.ValidateWww(www, root, Rules, checkAccess: true);

        Assert.True(check.IsValid, check.Detail);
        Assert.Equal(["blog"], Directory.GetFileSystemEntries(www).Select(e => Path.GetFileName(e)).ToArray());
    }

    [Fact]
    public void ValidateWww_SameAsRoot_IsRefused()
    {
        var root = Path.Combine(_temp.Path, "StackR");

        Assert.Equal(LocationProblem.WwwIsRoot, InstallLocationValidator.ValidateWww(root + "\\", root, Rules, false).Problem);
    }

    [Fact]
    public void ValidateWww_ParentOfRoot_IsRefused()
    {
        var root = Path.Combine(_temp.Path, "StackR");

        Assert.Equal(LocationProblem.WwwContainsRoot, InstallLocationValidator.ValidateWww(_temp.Path, root, Rules, false).Problem);
    }

    [Theory]
    [InlineData("bin")]
    [InlineData(@"data\ssl")]
    [InlineData("tmp")]
    public void ValidateWww_InsideInternalFolder_IsRefused(string relative)
    {
        var root = Path.Combine(_temp.Path, "StackR");

        var check = InstallLocationValidator.ValidateWww(Path.Combine(root, relative), root, Rules, false);

        Assert.Equal(LocationProblem.WwwInsideInternalFolder, check.Problem);
    }

    [Fact]
    public void ValidateWww_DefaultUnderRoot_IsAllowed()
    {
        var root = Path.Combine(_temp.Path, "StackR");

        var check = InstallLocationValidator.ValidateWww(Path.Combine(root, "www"), root, Rules, checkAccess: true);

        Assert.True(check.IsValid, check.Detail);
        Assert.False(Directory.Exists(root));
    }

    [Theory]
    [InlineData(@"C:\StackR\www", @"C:\StackR", true)]
    [InlineData(@"C:\stackr", @"C:\StackR\", true)]
    [InlineData(@"C:\StackR2", @"C:\StackR", false)]
    [InlineData(@"C:\Stack", @"C:\StackR", false)]
    public void IsSameOrUnder_ComparesWholeSegments(string path, string parent, bool expected)
    {
        Assert.Equal(expected, InstallLocationValidator.IsSameOrUnder(path, parent));
    }
}
