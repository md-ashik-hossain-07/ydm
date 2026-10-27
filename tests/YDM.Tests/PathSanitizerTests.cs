using System.IO;
using System.Security;
using Xunit;
using YDM.Core.Security;

namespace YDM.Tests;

public class PathSanitizerTests
{
    [Theory]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("..\\..\\Windows\\System32\\cmd.exe", "cmd.exe")]
    [InlineData("folder/subfolder/archive.zip", "archive.zip")]
    [InlineData("test<file>:name*?.txt", "test_file__name__.txt")]
    [InlineData("CON.txt", "_CON.txt")]
    [InlineData("NUL", "_NUL")]
    [InlineData("com1.dat", "_com1.dat")]
    [InlineData("aux.tar.gz", "_aux.tar.gz")]
    [InlineData("", "download.dat")]
    [InlineData("   ", "download.dat")]
    [InlineData("...", "download.dat")]
    public void SanitizeFileName_CleansUnsafeNames(string input, string expected)
    {
        string actual = PathSanitizer.SanitizeFileName(input);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void GetSafeDestinationPath_PreventsPathTraversal()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "YDM_Test_SafePath");
        Directory.CreateDirectory(tempDir);

        try
        {
            string safePath = PathSanitizer.GetSafeDestinationPath(tempDir, "file.txt");
            Assert.StartsWith(tempDir, safePath);
            Assert.Equal("file.txt", Path.GetFileName(safePath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void GetSafeDestinationPath_HandlesCollisions()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "YDM_Test_Collisions");
        Directory.CreateDirectory(tempDir);

        try
        {
            string file1 = Path.Combine(tempDir, "test.dat");
            File.WriteAllText(file1, "existing");

            string file2 = PathSanitizer.GetSafeDestinationPath(tempDir, "test.dat");
            Assert.Equal("test (1).dat", Path.GetFileName(file2));

            File.WriteAllText(file2, "second");
            string file3 = PathSanitizer.GetSafeDestinationPath(tempDir, "test.dat");
            Assert.Equal("test (2).dat", Path.GetFileName(file3));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
