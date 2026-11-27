using StackR.Core.Services;

namespace StackR.Core.Tests.Services;

public class MySqlDatabasesTests
{
    [Fact]
    public void Parse_BatchOutput_ReadsNameSizeTablesAndSystemFlag()
    {
        var list = MySqlDatabases.Parse("blog\t1589248\t12\r\nmysql\t2637824\t38\nvide\t0\t0\n\n");
        Assert.Equal(3, list.Count);
        Assert.Equal(new DatabaseInfo("blog", 1589248, 12, false), list[0]);
        Assert.True(list[1].IsSystem);
        Assert.Equal(0, list[2].Tables);
    }

    [Fact]
    public void Parse_GarbageLines_AreIgnored() =>
        Assert.Empty(MySqlDatabases.Parse("mysql: [Warning] Using a password\nfoo"));

    [Theory]
    [InlineData("blog", true)]
    [InlineData("mon_projet_2", true)]
    [InlineData("", false)]
    [InlineData("12345", false)]
    [InlineData("a-b", false)]
    [InlineData("drop`;", false)]
    [InlineData("café", false)]
    [InlineData("mysql", false)]
    [InlineData("Information_Schema", false)]
    public void ValidateName_Rules(string name, bool ok) => Assert.Equal(ok, MySqlDatabases.ValidateName(name) is null);

    [Fact]
    public void ValidateName_TooLong_IsRefused() => Assert.NotNull(MySqlDatabases.ValidateName(new string('a', 65)));

    [Fact]
    public void CreateStatement_UsesUtf8mb4AndBackticks() =>
        Assert.Equal(
            "CREATE DATABASE `blog` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci",
            MySqlDatabases.CreateStatement("blog"));

    [Fact]
    public void CreateStatement_InvalidName_Throws() =>
        Assert.Throws<ArgumentException>(() => MySqlDatabases.CreateStatement("x`; DROP DATABASE mysql; --"));

    [Fact]
    public void ListQuery_IncludesEmptyDatabases() =>
        Assert.Contains("LEFT JOIN", MySqlDatabases.ListQuery, StringComparison.Ordinal);
}
