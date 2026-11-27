using StackR.Core.Sites;

namespace StackR.Core.Tests.Sites;

public class SiteRemovalTests
{
    [Fact]
    public void Validate_DirectChildOfWww_IsAccepted()
    {
        using var temp = new TempDirectory();
        var site = Directory.CreateDirectory(Path.Combine(temp.Path, "www", "blog")).FullName;
        Assert.Null(SiteRemoval.Validate(Path.Combine(temp.Path, "www"), site));
    }

    [Fact]
    public void Validate_WwwItselfNestedOrOutside_IsRefused()
    {
        using var temp = new TempDirectory();
        var www = Directory.CreateDirectory(Path.Combine(temp.Path, "www")).FullName;
        var nested = Directory.CreateDirectory(Path.Combine(www, "blog", "public")).FullName;
        var outside = Directory.CreateDirectory(Path.Combine(temp.Path, "autre")).FullName;
        Assert.NotNull(SiteRemoval.Validate(www, www));
        Assert.NotNull(SiteRemoval.Validate(www, nested));
        Assert.NotNull(SiteRemoval.Validate(www, outside));
        Assert.NotNull(SiteRemoval.Validate(www, Path.Combine(www, "..", "autre")));
        Assert.NotNull(SiteRemoval.Validate(www, ""));
    }

    [Fact]
    public void Validate_MissingFolder_IsRefused()
    {
        using var temp = new TempDirectory();
        var www = Directory.CreateDirectory(Path.Combine(temp.Path, "www")).FullName;
        Assert.NotNull(SiteRemoval.Validate(www, Path.Combine(www, "absent")));
    }

    [Fact]
    public void MoveToRecycleBin_InvalidTarget_ThrowsWithoutTouchingAnything()
    {
        using var temp = new TempDirectory();
        var www = Directory.CreateDirectory(Path.Combine(temp.Path, "www")).FullName;
        Assert.Throws<InvalidOperationException>(() => SiteRemoval.MoveToRecycleBin(www, www));
        Assert.True(Directory.Exists(www));
    }
}
