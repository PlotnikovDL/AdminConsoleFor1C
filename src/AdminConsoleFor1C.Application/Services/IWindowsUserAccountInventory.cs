namespace AdminConsoleFor1C.Application.Services;

public interface IWindowsUserAccountInventory
{
    string CurrentUserName { get; }
    Task<IReadOnlyList<string>> GetUserNamesAsync(CancellationToken cancellationToken = default);
}
