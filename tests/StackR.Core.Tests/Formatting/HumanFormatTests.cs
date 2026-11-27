using System.Globalization;
using StackR.Core.Formatting;

namespace StackR.Core.Tests.Formatting;

public class HumanFormatTests
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");

    [Theory]
    [InlineData(512, "512 o")]
    [InlineData(1536, "1,5 Ko")]
    [InlineData(36_700_160, "35 Mo")]
    [InlineData(1_288_490_189, "1,2 Go")]
    public void Bytes_French_UsesOctets(long bytes, string expected) => Assert.Equal(expected, HumanFormat.Bytes(bytes, Fr));

    [Fact]
    public void Bytes_English_UsesBytes() => Assert.Equal("1.5 KB", HumanFormat.Bytes(1536, En));

    [Fact]
    public void Duration_Ranges_AreReadable()
    {
        Assert.Equal("45 s", HumanFormat.Duration(TimeSpan.FromSeconds(45), Fr));
        Assert.Equal("12 min", HumanFormat.Duration(TimeSpan.FromMinutes(12.5), Fr));
        Assert.Equal("2 h 05 min", HumanFormat.Duration(new TimeSpan(2, 5, 30), Fr));
        Assert.Equal("3 j 4 h", HumanFormat.Duration(new TimeSpan(3, 4, 0, 0), Fr));
        Assert.Equal("3 d 4 h", HumanFormat.Duration(new TimeSpan(3, 4, 0, 0), En));
        Assert.Equal("0 s", HumanFormat.Duration(TimeSpan.FromSeconds(-3), Fr));
    }

    [Fact]
    public void Ago_Ranges_AreReadable()
    {
        var now = new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal("à l’instant", HumanFormat.Ago(now.AddSeconds(-20), now, Fr));
        Assert.Equal("il y a 5 min", HumanFormat.Ago(now.AddMinutes(-5), now, Fr));
        Assert.Equal("il y a 3 h", HumanFormat.Ago(now.AddHours(-3), now, Fr));
        Assert.Equal("hier", HumanFormat.Ago(now.AddDays(-1.2), now, Fr));
        Assert.Equal("il y a 4 jours", HumanFormat.Ago(now.AddDays(-4), now, Fr));
        Assert.Equal("4 days ago", HumanFormat.Ago(now.AddDays(-4), now, En));
        Assert.Contains("2026", HumanFormat.Ago(now.AddDays(-60), now, Fr), StringComparison.Ordinal);
    }
}
