using System.Diagnostics;
using AdminConsoleFor1C.Core.Services;
using AdminConsoleFor1C.Infrastructure.Services;

namespace AdminConsoleFor1C.Application.Tests;

public sealed class OneCPlatformVersionResolverTests : IDisposable
{
    private readonly string root = Path.Combine(AppContext.BaseDirectory, "version-resolver-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void ReadsVersionFromExecutableInNonstandardDirectory()
    {
        var path = CopyVersionedAssembly("custom-server", "ragent.exe");

        Assert.Equal(ReadAssemblyVersion(), OneCPlatformVersionResolver.ResolveExecutableVersion(path));
        Assert.Equal(ReadAssemblyVersion(), OneCPlatformVersionResolver.ResolveExecutableVersion($"  {path}  "));
    }

    [Fact]
    public void FileMetadataTakesPrecedenceOverVersionDirectoryName()
    {
        var path = CopyVersionedAssembly(Path.Combine("8.3.99.9999", "bin"), "ragent.exe");

        Assert.Equal(ReadAssemblyVersion(), OneCPlatformVersionResolver.ResolveExecutableVersion(path));
        Assert.NotEqual("8.3.99.9999", OneCPlatformVersionResolver.ResolveExecutableVersion(path));
    }

    [Fact]
    public void UsesVersionDirectoryWhenExecutableIsMissing()
    {
        var path = Path.Combine(root, "8.3.27.2170", "bin", "ragent.exe");

        Assert.Equal("8.3.27.2170", OneCPlatformVersionResolver.ResolveExecutableVersion(path));
    }

    [Fact]
    public void DoesNotTreatMissingFileMetadataAsVersionZero()
    {
        var directory = Path.Combine(root, "8.3.27.2170", "bin");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "ragent.exe");
        File.WriteAllText(path, "no version resource");

        Assert.Equal("8.3.27.2170", OneCPlatformVersionResolver.ResolveExecutableVersion(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("missing-ragent.exe")]
    public void ReturnsUnknownWhenNeitherSourceProvidesVersion(string? path)
    {
        Assert.Null(OneCPlatformVersionResolver.ResolveExecutableVersion(path));
    }

    public void Dispose()
    {
        var fullRoot = Path.GetFullPath(root);
        var allowedRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "version-resolver-tests")) + Path.DirectorySeparatorChar;
        if (!fullRoot.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Test directory is outside the test output.");
        }
        if (Directory.Exists(fullRoot)) Directory.Delete(fullRoot, recursive: true);
    }

    private string CopyVersionedAssembly(string relativeDirectory, string name)
    {
        var directory = Path.Combine(root, relativeDirectory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        File.Copy(typeof(OneCServiceInfo).Assembly.Location, path);
        return path;
    }

    private static string ReadAssemblyVersion()
    {
        var version = FileVersionInfo.GetVersionInfo(typeof(OneCServiceInfo).Assembly.Location);
        Assert.True(version.FileMajorPart > 0);
        return $"{version.FileMajorPart}.{version.FileMinorPart}.{version.FileBuildPart}.{version.FilePrivatePart}";
    }
}
