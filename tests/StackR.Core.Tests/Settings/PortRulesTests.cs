using StackR.Core.Settings;

namespace StackR.Core.Tests.Settings;

public class PortRulesTests
{
    [Theory]
    [InlineData(1, true)]
    [InlineData(65535, true)]
    [InlineData(0, false)]
    [InlineData(65536, false)]
    [InlineData(-5, false)]
    public void IsValid_Port_ReturnsExpected(int port, bool expected) =>
        Assert.Equal(expected, PortRules.IsValid(port));

    [Fact]
    public void FindDuplicate_RepeatedPort_ReturnsIt() =>
        Assert.Equal(80, PortRules.FindDuplicate([80, 443, 80]));

    [Fact]
    public void FindDuplicate_DistinctPorts_ReturnsNull() =>
        Assert.Null(PortRules.FindDuplicate([80, 443, 3306]));
}
