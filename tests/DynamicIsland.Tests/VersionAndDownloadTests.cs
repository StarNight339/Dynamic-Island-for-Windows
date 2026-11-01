using DynamicIsland.Core;
using DynamicIsland.Providers;
using Xunit;

namespace DynamicIsland.Tests;

public sealed class VersionAndDownloadTests
{
    [Theory]
    [InlineData("v0.4.1-F", "0.4.1")]
    [InlineData("v0.4.0", "0.4.0")]
    [InlineData("0.3.9", "0.3.9")]
    [InlineData("V1.2", "1.2.0")]
    [InlineData("v0.9.9-M", "0.9.9")]
    public void Release_tags_parse_without_the_change_type_letter(string tag, string expected) =>
        Assert.Equal(Version.Parse(expected), Updater.ParseTag(tag));

    [Theory]
    [InlineData("pre-release")]
    [InlineData("")]
    [InlineData("latest")]
    public void Non_version_tags_are_ignored(string tag) => Assert.Null(Updater.ParseTag(tag));

    [Fact]
    public void Patch_bump_is_newer_regardless_of_letter() =>
        Assert.True(Updater.ParseTag("v0.4.2-B") > Updater.ParseTag("v0.4.1-F"));

    [Theory]
    [InlineData(@"C:\Users\me\Downloads\report.pdf.crdownload", true)]
    [InlineData(@"C:\Users\me\Downloads\setup.exe.part", true)]
    [InlineData(@"C:\Users\me\Downloads\movie.MP4.PART", true)]
    [InlineData(@"C:\Users\me\Downloads\report.pdf", false)]
    [InlineData(@"C:\Users\me\Downloads\notes.txt", false)]
    public void Detects_browser_temp_files(string path, bool expected) =>
        Assert.Equal(expected, DownloadProvider.IsTempDownload(path));

    [Theory]
    [InlineData(@"C:\d\report.pdf.crdownload", "report.pdf")]
    [InlineData(@"C:\d\setup.exe.part", "setup.exe")]
    [InlineData(@"C:\d\Unconfirmed 123456.crdownload", null)]
    public void Display_name_strips_the_temp_extension(string path, string? expected) =>
        Assert.Equal(expected, DownloadProvider.DisplayName(path));

    [Theory]
    [InlineData(512, "1 KB")]
    [InlineData(5 * 1024 * 1024, "5.0 MB")]
    [InlineData(3L * 1024 * 1024 * 1024, "3.00 GB")]
    public void Formats_sizes(long bytes, string expected) => Assert.Equal(expected, DownloadProvider.FormatSize(bytes));
}
