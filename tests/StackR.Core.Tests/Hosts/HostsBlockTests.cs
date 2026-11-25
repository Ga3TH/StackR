using StackR.Core.Hosts;

namespace StackR.Core.Tests.Hosts;

public class HostsBlockTests
{
    private const string Start = "# >>> StackR";
    private const string End = "# <<< StackR";

    private static string Block(params string[] hosts)
    {
        var body = string.Concat(hosts.Select(h => $"127.0.0.1 {h}\r\n"));
        return $"{Start}\r\n{body}{End}\r\n";
    }

    [Fact]
    public void Apply_NoBlock_AppendsBlock()
    {
        const string existing = "127.0.0.1 localhost\r\n::1 localhost\r\n";

        var result = HostsBlock.Apply(existing, ["b.test", "a.test"]);

        Assert.Equal(existing + Block("a.test", "b.test"), result);
    }

    [Fact]
    public void Apply_NoBlockNoTrailingNewline_AddsCrlfBeforeBlock()
    {
        const string existing = "127.0.0.1 localhost";

        var result = HostsBlock.Apply(existing, ["a.test"]);

        Assert.Equal(existing + "\r\n" + Block("a.test"), result);
    }

    [Fact]
    public void Apply_EmptyContent_ReturnsOnlyBlock()
    {
        Assert.Equal(Block("a.test"), HostsBlock.Apply("", ["a.test"]));
    }

    [Fact]
    public void Apply_ExistingBlock_ReplacesOnlyBlock()
    {
        const string before = "# en-tête\r\n10.0.0.5 db.lan\r\n";
        const string after = "# fin\r\n192.168.1.2 nas\r\n";
        var existing = before + Block("old.test", "z.test") + after;

        var result = HostsBlock.Apply(existing, ["new.test"]);

        Assert.Equal(before + Block("new.test") + after, result);
    }

    [Fact]
    public void Apply_Idempotent()
    {
        const string existing = "# a\n127.0.0.1 localhost\n# b";
        string[] hosts = ["site-b.test", "site-a.test"];

        var once = HostsBlock.Apply(existing, hosts);
        var twice = HostsBlock.Apply(once, hosts);

        Assert.Equal(once, twice);
    }

    [Fact]
    public void Apply_EmptyList_RemovesBlock()
    {
        const string before = "127.0.0.1 localhost\r\n";
        const string after = "# après\r\n";
        var existing = before + Block("a.test", "b.test") + after;

        var result = HostsBlock.Apply(existing, []);

        Assert.Equal(before + after, result);
    }

    [Fact]
    public void Apply_EmptyListNoBlock_ReturnsContentUnchanged()
    {
        const string existing = "# rien\n\t127.0.0.1   localhost   \r\n";

        Assert.Equal(existing, HostsBlock.Apply(existing, []));
    }

    [Fact]
    public void Apply_RoundTrip_RestoresOriginalExactly()
    {
        const string original = "# Copyright (c) Microsoft\r\n#\r\n127.0.0.1 localhost\n";

        var added = HostsBlock.Apply(original, ["a.test"]);
        var removed = HostsBlock.Apply(added, []);

        Assert.Equal(original, removed);
    }

    [Fact]
    public void Apply_PreservesForeignLinesExactly()
    {
        // Mélange LF/CRLF, tabulations, espaces de fin, Unicode, CR isolé, pas de fin de ligne finale.
        const string before = "# Hôtes perso — été\n\t10.0.0.1\tdb.lan   \r\n\r\n   \n0.0.0.0 ads.example # bloqué\r\nweird\rline\n";
        const string after = "\n# fin sans saut de ligne\t ";
        var existing = before + Block("old.test") + after;

        var result = HostsBlock.Apply(existing, ["x.test"]);

        Assert.Equal(before + Block("x.test") + after, result);
    }

    [Theory]
    [InlineData("evil.com")]
    [InlineData("a.test\r\n1.2.3.4 bank.com")]
    [InlineData("a.test\n")]
    [InlineData("a.test\r")]
    [InlineData("")]
    [InlineData(" a.test")]
    [InlineData("a.test ")]
    [InlineData("a .test")]
    [InlineData("a\t.test")]
    [InlineData(".test")]
    [InlineData("-a.test")]
    [InlineData("a-.test")]
    [InlineData("a..test")]
    [InlineData("a.b.test")]
    [InlineData("a_b.test")]
    [InlineData("été.test")]
    [InlineData("K.test")] // signe Kelvin : ne doit pas devenir « k »
    [InlineData("ａ.test")] // a pleine chasse
    [InlineData("a.test.com")]
    [InlineData("a.testx")]
    [InlineData("localhost")]
    [InlineData("a.test#")]
    [InlineData("a.test\0")]
    public void Apply_InvalidHost_Throws(string host)
    {
        Assert.Throws<ArgumentException>(() => HostsBlock.Apply("", [host]));
    }

    [Fact]
    public void Apply_InvalidHostAmongValid_ThrowsAndValidatesAll()
    {
        Assert.Throws<ArgumentException>(() => HostsBlock.Apply("", ["a.test", "evil.com", "b.test"]));
    }

    [Fact]
    public void Apply_NullHostInList_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => HostsBlock.Apply("", ["a.test", null!]));
    }

    [Fact]
    public void Apply_LabelTooLong_Throws()
    {
        var label64 = new string('a', 64);
        Assert.Throws<ArgumentException>(() => HostsBlock.Apply("", [label64 + ".test"]));
    }

    [Fact]
    public void Apply_LabelOf63_Accepted()
    {
        var label63 = new string('a', 63);
        Assert.Equal(Block(label63 + ".test"), HostsBlock.Apply("", [label63 + ".test"]));
    }

    [Fact]
    public void Apply_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => HostsBlock.Apply(null!, ["a.test"]));
        Assert.Throws<ArgumentNullException>(() => HostsBlock.Apply("", null!));
    }

    [Fact]
    public void Apply_MixedCaseAndDuplicates_NormalizedSortedUnique()
    {
        var result = HostsBlock.Apply("", ["B.test", "a.test", "A.TEST", "b.test", "c-1.Test"]);

        Assert.Equal(Block("a.test", "b.test", "c-1.test"), result);
    }

    [Fact]
    public void Apply_DuplicatedBlocks_CollapsedIntoOneAtFirstPosition()
    {
        const string a = "# a\r\n";
        const string b = "# b\r\n";
        const string c = "# c\r\n";
        var existing = a + Block("x.test") + b + Block("y.test") + c;

        var result = HostsBlock.Apply(existing, ["z.test"]);

        Assert.Equal(a + Block("z.test") + b + c, result);
    }

    [Fact]
    public void Apply_UnterminatedStart_RemovesOnlyOwnEntriesAndKeepsForeignLines()
    {
        const string existing =
            "# avant\r\n" +
            Start + "\r\n" +
            "127.0.0.1 old.test\r\n" +
            "127.0.0.1 old2.test\r\n" +
            "10.0.0.1 db.lan\r\n" +
            "127.0.0.1 later.test\r\n" +
            "1.2.3.4 bank.com\r\n";

        var result = HostsBlock.Apply(existing, ["new.test"]);

        Assert.Equal(
            "# avant\r\n" +
            Block("new.test") +
            "10.0.0.1 db.lan\r\n" +
            "127.0.0.1 later.test\r\n" +
            "1.2.3.4 bank.com\r\n",
            result);
    }

    [Fact]
    public void Apply_UnterminatedStartWithEmptyList_KeepsForeignLines()
    {
        const string existing = Start + "\r\n127.0.0.1 old.test\r\n1.2.3.4 bank.com\r\n";

        var result = HostsBlock.Apply(existing, []);

        Assert.Equal("1.2.3.4 bank.com\r\n", result);
    }

    [Fact]
    public void Apply_StartFollowedByAnotherBlock_FirstTreatedAsUnterminated()
    {
        const string existing =
            Start + "\r\n" +
            "127.0.0.1 a.test\r\n" +
            "# commentaire étranger\r\n" +
            Start + "\r\n" +
            "127.0.0.1 b.test\r\n" +
            End + "\r\n";

        var result = HostsBlock.Apply(existing, ["c.test"]);

        Assert.Equal(Block("c.test") + "# commentaire étranger\r\n", result);
    }

    [Fact]
    public void Apply_StrayEndMarker_IsRemoved()
    {
        const string existing = "# a\r\n" + End + "\r\n# b\r\n";

        var result = HostsBlock.Apply(existing, ["x.test"]);

        Assert.Equal("# a\r\n" + Block("x.test") + "# b\r\n", result);
    }

    [Fact]
    public void Apply_ForeignLineInsideBlock_IsPreservedAfterBlock()
    {
        const string existing =
            "# a\r\n" +
            Start + "\r\n" +
            "127.0.0.1 old.test\r\n" +
            "1.2.3.4 bank.com\r\n" +
            "# note\r\n" +
            End + "\r\n" +
            "# b\r\n";

        var result = HostsBlock.Apply(existing, ["new.test"]);

        Assert.Equal("# a\r\n" + Block("new.test") + "1.2.3.4 bank.com\r\n# note\r\n# b\r\n", result);
        Assert.Equal(result, HostsBlock.Apply(result, ["new.test"]));
    }

    [Fact]
    public void Apply_BlockWithLfAndExtraWhitespace_IsRecognized()
    {
        const string existing =
            "# a\n" +
            "  # >>> StackR \t\n" +
            "127.0.0.1\told.test  \n" +
            "# <<< StackR\n" +
            "# b\n";

        var result = HostsBlock.Apply(existing, ["new.test"]);

        Assert.Equal("# a\n" + Block("new.test") + "# b\n", result);
    }

    [Fact]
    public void Apply_BlockAtEndWithoutFinalNewline_IsReplaced()
    {
        const string existing = "# a\r\n" + Start + "\r\n127.0.0.1 old.test\r\n" + End;

        var result = HostsBlock.Apply(existing, ["new.test"]);

        Assert.Equal("# a\r\n" + Block("new.test"), result);
    }

    [Theory]
    [InlineData("## >>> StackR")]
    [InlineData("# >>> StackRX")]
    [InlineData("# >>> stackr")]
    [InlineData("#>>> StackR")]
    [InlineData("x # >>> StackR")]
    public void Apply_MarkerLookalike_IsNotTreatedAsMarker(string lookalike)
    {
        var existing = lookalike + "\r\n127.0.0.1 keep.test\r\n";

        var result = HostsBlock.Apply(existing, ["a.test"]);

        Assert.Equal(existing + Block("a.test"), result);
    }

    [Fact]
    public void Apply_NonLoopbackTestEntryInUnterminatedRun_IsKept()
    {
        const string existing = Start + "\r\n10.0.0.1 x.test\r\n127.0.0.1 evil.com\r\n127.0.0.1 a.test b.test\r\n";

        var result = HostsBlock.Apply(existing, []);

        Assert.Equal("10.0.0.1 x.test\r\n127.0.0.1 evil.com\r\n127.0.0.1 a.test b.test\r\n", result);
    }

    [Theory]
    [InlineData("a.test", true)]
    [InlineData("0.test", true)]
    [InlineData("my-site-2.test", true)]
    [InlineData("a-b.test", true)]
    [InlineData("A.test", false)]
    [InlineData("a.TEST", false)]
    [InlineData("a.test\n", false)]
    [InlineData("evil.com", false)]
    [InlineData("", false)]
    [InlineData("-.test", false)]
    [InlineData("a.b.test", false)]
    public void IsValidHost_Cases(string host, bool expected)
    {
        Assert.Equal(expected, HostsBlock.IsValidHost(host));
    }

    [Fact]
    public void IsValidHost_Null_ReturnsFalse()
    {
        Assert.False(HostsBlock.IsValidHost(null!));
    }
}
