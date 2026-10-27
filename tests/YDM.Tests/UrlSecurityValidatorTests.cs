using System.Net;
using Xunit;
using YDM.Core.Security;

namespace YDM.Tests;

public class UrlSecurityValidatorTests
{
    [Theory]
    [InlineData("http://example.com/file.zip", true)]
    [InlineData("https://releases.ubuntu.com/jammy/ubuntu.iso", true)]
    [InlineData("ftp://ftp.example.com/file.zip", false)]
    [InlineData("file:///C:/Windows/System32/cmd.exe", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("http://localhost:8080/secret", false)]
    [InlineData("http://127.0.0.1/admin", false)]
    [InlineData("http://169.254.169.254/latest/meta-data/", false)]
    [InlineData("http://10.0.0.1/internal", false)]
    [InlineData("http://192.168.1.1/router", false)]
    [InlineData("http://172.16.0.1/intranet", false)]
    public void IsValidDownloadUrl_EnforcesSecurityRules(string url, bool expectedValid)
    {
        bool actual = UrlSecurityValidator.IsValidDownloadUrl(url, allowLocal: false);
        Assert.Equal(expectedValid, actual);
    }

    [Fact]
    public void IsValidDownloadUrl_AllowsLocalWhenFlagExplicitlyEnabled()
    {
        bool actual = UrlSecurityValidator.IsValidDownloadUrl("http://127.0.0.1:5000/test.dat", allowLocal: true);
        Assert.True(actual);
    }
}
