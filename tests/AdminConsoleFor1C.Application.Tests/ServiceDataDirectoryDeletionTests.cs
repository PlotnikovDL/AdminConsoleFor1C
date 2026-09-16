using AdminConsoleFor1C.Infrastructure.Services;
using System.Diagnostics;

namespace AdminConsoleFor1C.Application.Tests;

public sealed class ServiceDataDirectoryDeletionTests
{
    [Fact]
    public void RejectsDirectoryJunctionAndItsDescendantsBeforeDeletingFiles()
    {
        var root = CreateTestDirectory();
        var target = Path.Combine(root, "2540");
        var external = Path.Combine(root, "keep");
        var link = Path.Combine(target, "link");
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(Path.Combine(external, "child"));
        var targetFile = Path.Combine(target, "keep.txt");
        var externalFile = Path.Combine(external, "keep.txt");
        File.WriteAllText(targetFile, "keep");
        File.WriteAllText(externalFile, "keep");
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "/c", "mklink", "/J", link, external }) start.ArgumentList.Add(argument);
        using (var process = Process.Start(start)!)
        {
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);
        }
        try
        {
            Assert.Throws<InvalidOperationException>(() => ServiceDataDirectoryDeletion.Delete(target));
            Assert.Throws<InvalidOperationException>(() => ServiceDataDirectoryDeletion.Delete(Path.Combine(link, "child")));
            Assert.Equal("keep", File.ReadAllText(targetFile));
            Assert.Equal("keep", File.ReadAllText(externalFile));
        }
        finally
        {
            Directory.Delete(link, recursive: false);
            File.Delete(targetFile);
            File.Delete(externalFile);
            Directory.Delete(Path.Combine(external, "child"));
            Directory.Delete(target);
            Directory.Delete(external);
            Directory.Delete(root);
        }
    }

    [Fact]
    public void DeletesOnlyRequestedTreeAndPreservesSibling()
    {
        var root = CreateTestDirectory();
        var target = Path.Combine(root, "2540");
        var sibling = Path.Combine(root, "25400");
        Directory.CreateDirectory(Path.Combine(target, "reg_2541"));
        Directory.CreateDirectory(sibling);
        var siblingFile = Path.Combine(sibling, "keep.txt");
        File.WriteAllText(siblingFile, "keep");
        File.WriteAllText(Path.Combine(target, "reg_2541", "test.lst"), "test cluster");
        ServiceDataDirectoryDeletion.Delete(target);
        Assert.False(Directory.Exists(target));
        Assert.Equal("keep", File.ReadAllText(siblingFile));
        File.Delete(siblingFile);
        Directory.Delete(sibling);
        Directory.Delete(root);
    }

    [Fact]
    public void RejectsFileAndAcceptsAlreadyMissingDirectory()
    {
        var root = CreateTestDirectory();
        var file = Path.Combine(root, "data.txt");
        File.WriteAllText(file, "keep");
        Assert.Throws<InvalidOperationException>(() => ServiceDataDirectoryDeletion.Delete(file));
        Assert.Equal("keep", File.ReadAllText(file));
        ServiceDataDirectoryDeletion.Delete(Path.Combine(root, "missing"));
        File.Delete(file);
        Directory.Delete(root);
    }

    private static string CreateTestDirectory()
    {
        // Bound all test mutations to a fresh subdirectory of this test assembly's output.
        var path = Path.Combine(AppContext.BaseDirectory, "deletion-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
