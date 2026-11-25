using Microsoft.Win32;
using StackR.Core.Components;

namespace StackR.Core.Tests.Components;

public sealed class PrerequisitesTests : IDisposable
{
    private readonly string _name = @"Software\StackR.Tests\" + Guid.NewGuid().ToString("N");
    private readonly RegistryKey _root;

    public PrerequisitesTests() => _root = Registry.CurrentUser.CreateSubKey(_name);

    public void Dispose()
    {
        _root.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_name, throwOnMissingSubKey: false);
    }

    private void Set(int installed, int major, int minor)
    {
        using var k = _root.CreateSubKey(@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64");
        k.SetValue("Installed", installed, RegistryValueKind.DWord);
        k.SetValue("Major", major, RegistryValueKind.DWord);
        k.SetValue("Minor", minor, RegistryValueKind.DWord);
    }

    [Fact]
    public void HasVcRedist_Missing_False() => Assert.False(Prerequisites.HasVcRedistX64(_root));

    [Fact]
    public void HasVcRedist_Vs18Build_True()
    {
        Set(1, 14, 50);
        Assert.True(Prerequisites.HasVcRedistX64(_root));
    }

    [Fact]
    public void HasVcRedist_OldMinor_False()
    {
        Set(1, 14, 44);
        Assert.False(Prerequisites.HasVcRedistX64(_root));
    }

    [Fact]
    public void HasVcRedist_NotInstalled_False()
    {
        Set(0, 14, 50);
        Assert.False(Prerequisites.HasVcRedistX64(_root));
    }

    [Fact]
    public void VcRedistUrl_IsHttpsAkaMs() =>
        Assert.Equal("https://aka.ms/vc14/vc_redist.x64.exe", Prerequisites.VcRedistUrl.ToString());
}
