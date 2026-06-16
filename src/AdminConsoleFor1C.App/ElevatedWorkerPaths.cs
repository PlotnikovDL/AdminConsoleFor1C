namespace AdminConsoleFor1C.App;

internal static class ElevatedWorkerPaths
{
    private const string WorkerFileName = "AdminConsoleFor1C.ElevatedWorker.exe";

    public static string ResolveWorkerPath()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDirectory, "ElevatedWorker", WorkerFileName),
            Path.Combine(baseDirectory, WorkerFileName),
            Path.Combine(baseDirectory, "..", "ElevatedWorker", WorkerFileName)
        };

        return candidates
            .Select(static path => Path.GetFullPath(path))
            .FirstOrDefault(File.Exists) ?? candidates[0];
    }

    public static string ResolveResultDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AdminConsoleFor1C",
            "Temp",
            "ElevatedActions");
    }
}
