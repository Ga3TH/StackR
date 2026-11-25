using StackR.Core.Security;

namespace StackR.Core.Tests.Security;

public class PasswordGeneratorTests
{
    [Fact]
    public void Generate_LengthAndAlphabet()
    {
        foreach (var len in new[] { 24, 8, 64 })
        {
            var p = len == 24 ? PasswordGenerator.Generate() : PasswordGenerator.Generate(len);
            Assert.Equal(len, p.Length);
            Assert.All(p, c => Assert.True(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'));
        }
    }

    [Fact]
    public void Generate_TwoCallsDiffer()
    {
        Assert.NotEqual(PasswordGenerator.Generate(), PasswordGenerator.Generate());
    }

    [Fact]
    public void Generate_NonPositiveLength_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PasswordGenerator.Generate(0));
    }
}
