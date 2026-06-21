namespace AdminConsoleFor1C.Core.Services;

public sealed record OneCServerAgentServiceCandidate
{
    public required string ExecutablePath { get; init; }

    public string? Version { get; init; }

    public string VersionText => string.IsNullOrWhiteSpace(Version) ? "—" : Version;

    public string ExecutablePathText => string.IsNullOrWhiteSpace(ExecutablePath) ? "—" : ExecutablePath;
}
