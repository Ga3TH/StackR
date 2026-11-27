using System.Text;
using System.Text.Json.Nodes;
using StackR.Core.Config;

namespace StackR.Core.Tests.Config;

public class HeidiSqlSettingsTests
{
    private static JsonObject Read(string dir) =>
        (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "settings.json")))!;

    // Décodage identique à decrypt() de HeidiSQL (variante 8 bits), pour vérifier l'aller-retour.
    private static string Decode(string s)
    {
        var salt = s[^1] - '0';
        var sb = new StringBuilder();
        for (var i = 0; i + 1 < s.Length; i += 2)
        {
            var nr = Convert.ToInt32(s.Substring(i, 2), 16) - salt;
            if (nr < 0)
            {
                nr += 255;
            }

            sb.Append((char)nr);
        }

        return sb.ToString();
    }

    [Fact]
    public void EncodePassword_KnownValue_MatchesHeidiSqlFormat()
    {
        // 'a' (0x61) + 3 = 0x64, 'B' (0x42) + 3 = 0x45, puis le sel.
        Assert.Equal("64453", HeidiSqlSettings.EncodePassword("aB", 3));
        Assert.Equal(string.Empty, HeidiSqlSettings.EncodePassword("", 3));
    }

    [Fact]
    public void EncodePassword_BeyondLatin1_UsesFourDigitsAndUnicodeFlag() =>
        Assert.Equal("006320AE20", HeidiSqlSettings.EncodePassword("a€", 2));

    [Theory]
    [InlineData("Xy9!zQ_-@#%^&*()", 1)]
    [InlineData("motdepasse", 9)]
    [InlineData("é£", 5)]
    public void EncodePassword_RoundTrips(string password, int salt) =>
        Assert.Equal(password, Decode(HeidiSqlSettings.EncodePassword(password, salt)));

    [Fact]
    public void EncodePassword_BadSalt_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => HeidiSqlSettings.EncodePassword("a", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => HeidiSqlSettings.EncodePassword("a", 10));
    }

    [Fact]
    public void WriteSession_CreatesSessionAndPortableLock()
    {
        using var tmp = new TempDirectory();
        HeidiSqlSettings.WriteSession(tmp.Path, 3307, "s3cret", salt: 4);

        var session = (JsonObject)Read(tmp.Path)["Servers"]!["StackR"]!;
        Assert.Equal("127.0.0.1", (string?)session["Host"]);
        Assert.Equal("3307", (string?)session["Port"]);
        Assert.Equal("root", (string?)session["User"]);
        Assert.Equal("s3cret", Decode((string)session["Password"]!));
        Assert.True(File.Exists(Path.Combine(tmp.Path, "portable.lock")));
    }

    [Fact]
    public void WriteSession_KeepsOtherSettingsAndSessions_AndUpdatesInPlace()
    {
        using var tmp = new TempDirectory();
        File.WriteAllText(Path.Combine(tmp.Path, "settings.json"),
            """{"FontSize":10,"Servers":{"Autre":{"Host":"db.example"},"StackR":{"Host":"old","Comment":"garde"}}}""");

        HeidiSqlSettings.WriteSession(tmp.Path, 3306, "pw");

        var root = Read(tmp.Path);
        Assert.Equal(10, (int?)root["FontSize"]);
        Assert.Equal("db.example", (string?)root["Servers"]!["Autre"]!["Host"]);
        Assert.Equal("127.0.0.1", (string?)root["Servers"]!["StackR"]!["Host"]);
        Assert.Equal("garde", (string?)root["Servers"]!["StackR"]!["Comment"]);
    }

    [Fact]
    public void WriteSession_NullPassword_LeavesPasswordEmpty()
    {
        using var tmp = new TempDirectory();
        HeidiSqlSettings.WriteSession(tmp.Path, 3306, null);
        Assert.Equal(string.Empty, (string?)Read(tmp.Path)["Servers"]!["StackR"]!["Password"]);
    }

    [Fact]
    public void ClearPassword_EmptiesOnlyThePassword()
    {
        using var tmp = new TempDirectory();
        HeidiSqlSettings.WriteSession(tmp.Path, 3306, "pw");
        HeidiSqlSettings.ClearPassword(tmp.Path);

        var session = Read(tmp.Path)["Servers"]!["StackR"]!;
        Assert.Equal(string.Empty, (string?)session["Password"]);
        Assert.Equal("root", (string?)session["User"]);
    }

    [Fact]
    public void ClearPassword_NoFile_DoesNothing()
    {
        using var tmp = new TempDirectory();
        HeidiSqlSettings.ClearPassword(tmp.Path);
        Assert.False(File.Exists(Path.Combine(tmp.Path, "settings.json")));
    }

    [Fact]
    public void WriteSession_CorruptFile_ThrowsAndLeavesItUntouched()
    {
        using var tmp = new TempDirectory();
        var file = Path.Combine(tmp.Path, "settings.json");
        File.WriteAllText(file, "{ pas du json");

        Assert.Throws<InvalidDataException>(() => HeidiSqlSettings.WriteSession(tmp.Path, 3306, "pw"));
        Assert.Equal("{ pas du json", File.ReadAllText(file));
    }
}
