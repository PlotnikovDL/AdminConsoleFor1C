namespace AdminConsoleFor1C.Core.Administration;

public sealed record OneCInfobasePropertiesSnapshot(
    OneCInfobaseEditTarget Target,
    IReadOnlyDictionary<string, string> Properties,
    IReadOnlyList<string> SecurityProfiles,
    string? SecurityProfilesWarning = null);
