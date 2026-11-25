using StackR.Core.Settings;

namespace StackR.Core.Tests.Settings;

public class SettingsStoreTests
{
    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        using var temp = new TempDirectory();
        var path = System.IO.Path.Combine(temp.Path, "stackr.json");

        var settings = SettingsStore.Load(path);

        Assert.Equal(@"C:\StackR", settings.Root);
        Assert.Equal(80, settings.HttpPort);
        Assert.Equal(443, settings.HttpsPort);
        Assert.Equal(3306, settings.MySqlPort);
        Assert.Equal(8025, settings.MailpitUiPort);
        Assert.Equal(1025, settings.MailpitSmtpPort);
        Assert.True(settings.HttpsEnabled);
        Assert.Equal("System", settings.Theme);
        Assert.Equal("fr", settings.Language);
        Assert.False(settings.FirstRunCompleted);
        Assert.Empty(settings.ActiveVersions);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        using var temp = new TempDirectory();
        var path = System.IO.Path.Combine(temp.Path, "sous dossier é", "stackr.json");
        var original = new StackRSettings
        {
            Root = @"C:\Dossier é\StackR",
            WwwPath = @"C:\laragon\www",
            ActiveVersions = new Dictionary<string, string> { ["php"] = "8.4.1", ["apache"] = "2.4.62" },
            HttpPort = 8080,
            StartWithWindows = true,
            AutoStartServices = true,
            Theme = "Dark",
            Language = "en",
            FirstRunCompleted = true,
        };

        SettingsStore.Save(path, original);
        var loaded = SettingsStore.Load(path);

        Assert.Equal(original.Root, loaded.Root);
        Assert.Equal(original.WwwPath, loaded.WwwPath);
        Assert.Equal(original.ActiveVersions, loaded.ActiveVersions);
        Assert.Equal(original.HttpPort, loaded.HttpPort);
        Assert.True(loaded.StartWithWindows);
        Assert.True(loaded.AutoStartServices);
        Assert.Equal("Dark", loaded.Theme);
        Assert.Equal("en", loaded.Language);
        Assert.True(loaded.FirstRunCompleted);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Load_CorruptJson_ReturnsDefaultsAndBacksUpFile()
    {
        using var temp = new TempDirectory();
        var path = System.IO.Path.Combine(temp.Path, "stackr.json");
        File.WriteAllText(path, "{ ceci n'est pas du json");

        var settings = SettingsStore.Load(path);

        Assert.Equal(80, settings.HttpPort);
        Assert.False(File.Exists(path));
        Assert.True(File.Exists(path + ".bak"));
    }

    [Fact]
    public void DefaultPath_IsUnderLocalAppData()
    {
        var expected = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StackR", "stackr.json");

        Assert.Equal(expected, SettingsStore.DefaultPath);
    }

    [Fact]
    public void Load_NullActiveVersions_NormalizedToEmpty()
    {
        using var temp = new TempDirectory();
        var path = System.IO.Path.Combine(temp.Path, "stackr.json");
        File.WriteAllText(path, "{ \"activeVersions\": null }");

        var settings = SettingsStore.Load(path);

        Assert.NotNull(settings.ActiveVersions);
        Assert.Empty(settings.ActiveVersions);
    }

    [Fact]
    public void Save_LeavesNoTempFile()
    {
        using var temp = new TempDirectory();
        var path = System.IO.Path.Combine(temp.Path, "sub", "stackr.json");
        SettingsStore.Save(path, new StackRSettings());
        Assert.Single(Directory.GetFiles(System.IO.Path.GetDirectoryName(path)!));
    }
}
