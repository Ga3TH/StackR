using System.Text;
using StackR.Core.IO;

namespace StackR.Core.Tests.IO;

public class LogTailTests
{
    [Fact]
    public void Poll_FirstCall_ReturnsLastLinesOnly()
    {
        using var temp = new TempDirectory();
        var file = Path.Combine(temp.Path, "a.log");
        File.WriteAllLines(file, Enumerable.Range(1, 1000).Select(i => $"ligne {i}"));

        var result = new LogTail(file, 500).Poll();

        Assert.True(result.Reset);
        Assert.Equal(500, result.Lines.Count);
        Assert.Equal("ligne 501", result.Lines[0]);
        Assert.Equal("ligne 1000", result.Lines[^1]);
    }

    [Fact]
    public void Poll_AppendedLines_ReturnsOnlyNewOnes()
    {
        using var temp = new TempDirectory();
        var file = Path.Combine(temp.Path, "a.log");
        File.WriteAllText(file, "un\ndeux\n");
        var tail = new LogTail(file);
        tail.Poll();

        File.AppendAllText(file, "trois\r\nquatre\n");
        var result = tail.Poll();

        Assert.False(result.Reset);
        Assert.Equal(["trois", "quatre"], result.Lines);
        Assert.Empty(tail.Poll().Lines);
    }

    [Fact]
    public void Poll_PartialLine_IsCompletedOnNextPoll()
    {
        using var temp = new TempDirectory();
        var file = Path.Combine(temp.Path, "a.log");
        File.WriteAllText(file, "début");
        var tail = new LogTail(file);
        Assert.Empty(tail.Poll().Lines);

        File.AppendAllText(file, " suite\n", Encoding.UTF8);

        Assert.Equal(["début suite"], tail.Poll().Lines);
    }

    [Fact]
    public void Poll_FileTruncated_ResetsAndReadsFromStart()
    {
        using var temp = new TempDirectory();
        var file = Path.Combine(temp.Path, "a.log");
        File.WriteAllText(file, "ancien 1\nancien 2\nancien 3\n");
        var tail = new LogTail(file);
        tail.Poll();

        File.WriteAllText(file, "neuf\n");
        var result = tail.Poll();

        Assert.True(result.Reset);
        Assert.Equal(["neuf"], result.Lines);
    }

    [Fact]
    public void Poll_MissingFile_ReturnsEmptyWithoutThrowing()
    {
        using var temp = new TempDirectory();
        var tail = new LogTail(Path.Combine(temp.Path, "absent.log"));

        var result = tail.Poll();

        Assert.Empty(result.Lines);
        Assert.False(result.Reset);
    }

    [Fact]
    public void Poll_FileOpenedForWriting_DoesNotFail()
    {
        using var temp = new TempDirectory();
        var file = Path.Combine(temp.Path, "a.log");
        using var writer = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.Read);
        writer.Write(Encoding.UTF8.GetBytes("é écrit\n"));
        writer.Flush();

        Assert.Equal(["é écrit"], new LogTail(file).Poll().Lines);
    }

    [Fact]
    public void Poll_LargeFile_OnlyReadsTheEnd()
    {
        using var temp = new TempDirectory();
        var file = Path.Combine(temp.Path, "big.log");
        using (var w = new StreamWriter(file))
        {
            for (var i = 1; i <= 300_000; i++)
            {
                w.WriteLine($"ligne {i} avec du remplissage pour grossir le fichier");
            }
        }

        var result = new LogTail(file, 500).Poll();

        Assert.Equal(500, result.Lines.Count);
        Assert.StartsWith("ligne 299501 ", result.Lines[0]);
        Assert.StartsWith("ligne 300000 ", result.Lines[^1]);
    }
}
