using AdminConsoleFor1C.Core.Administration;

namespace AdminConsoleFor1C.Application.Services;

public interface IOneCInfobaseEditorClient
{
    Task<OneCInfobasePropertiesSnapshot> ReadAsync(OneCServerConnectionProfile profile,
        OneCInfobaseEditTarget target, string? clusterPassword, string? infobaseUser, string? infobasePassword,
        CancellationToken cancellationToken = default);

    Task<OneCInfobasePropertiesSnapshot> UpdateAsync(OneCServerConnectionProfile profile,
        OneCInfobaseEditTarget target, OneCInfobasePropertiesSnapshot originalSnapshot,
        IReadOnlyDictionary<string, string> changes, string? clusterPassword, string? infobaseUser,
        string? infobasePassword, string? newDatabasePassword, CancellationToken cancellationToken = default);
}

public sealed class OneCInfobaseUpdateConflictException(string message) : Exception(message);

// Once an update was sent, losing its response or confirmation cannot prove that it failed.
public sealed class OneCInfobaseUpdateUncertainException(string message) : Exception(message);
