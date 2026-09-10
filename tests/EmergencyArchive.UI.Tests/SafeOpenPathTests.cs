using System.IO;
using EmergencyArchive.UI;
using Xunit;

namespace EmergencyArchive.UI.Tests;

/// <summary>
/// Finding 4.1: a document's name comes from decrypted vault content, so the
/// OPEN temp path must never escape the working directory or hit a reserved
/// device name, regardless of how hostile the stored name is.
/// </summary>
public class SafeOpenPathTests
{
    [Theory]
    [InlineData("report.pdf", "report.pdf")]
    [InlineData("..\\..\\evil.exe", "evil.exe")]
    [InlineData("../../evil.exe", "evil.exe")]
    [InlineData("/etc/passwd", "passwd")]
    [InlineData("C:\\Windows\\System32\\cmd.exe", "cmd.exe")]
    [InlineData("a/b/c/deep.txt", "deep.txt")]
    [InlineData("na:me?.txt", "na_me_.txt")]   // illegal chars replaced
    [InlineData("trailing.   ", "trailing")]    // trailing dots/spaces stripped
    public void SanitizeLeafFileName_ReducesToSafeLeaf(string input, string expected)
    {
        Assert.Equal(expected, MainWindowViewModel.SanitizeLeafFileName(input));
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("nul.txt")]
    [InlineData("COM1")]
    [InlineData("lpt9.dat")]
    public void SanitizeLeafFileName_EscapesReservedDeviceNames(string input)
    {
        string result = MainWindowViewModel.SanitizeLeafFileName(input);
        Assert.StartsWith("_", result);
    }

    [Fact]
    public void SanitizeLeafFileName_EmptyOrDots_FallsBackToDocument()
    {
        Assert.Equal("document", MainWindowViewModel.SanitizeLeafFileName("..."));
        Assert.Equal("document", MainWindowViewModel.SanitizeLeafFileName("   "));
        Assert.Equal("document", MainWindowViewModel.SanitizeLeafFileName(""));
    }

    [Fact]
    public void BuildSafeOpenTargetPath_StaysInsideTempDirectory_ForHostileNames()
    {
        var vm = new MainWindowViewModel();
        try
        {
            string temp = vm.EnsureOpenTempDirectory();
            string tempRoot = Path.GetFullPath(temp) + Path.DirectorySeparatorChar;

            foreach (string hostile in new[] { "..\\..\\escape.exe", "/etc/shadow", "C:\\Windows\\x.dll", "normal.pdf" })
            {
                string target = vm.BuildSafeOpenTargetPath(hostile);
                Assert.StartsWith(tempRoot, target);          // never escapes temp
                Assert.Equal(temp, Path.GetDirectoryName(target)); // directly inside it
            }
        }
        finally
        {
            vm.Lock(); // cleans up the temp directory
        }
    }
}
