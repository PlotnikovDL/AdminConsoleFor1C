using System.Management;
using System.Runtime.Versioning;
using System.Security.Principal;
using AdminConsoleFor1C.Application.Services;

namespace AdminConsoleFor1C.Infrastructure.Services;

[SupportedOSPlatform("windows")]
public sealed class WindowsUserAccountInventory : IWindowsUserAccountInventory
{
    public string CurrentUserName
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.Name;
        }
    }

    public Task<IReadOnlyList<string>> GetUserNamesAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<string>>(() =>
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { CurrentUserName };
            using var searcher = new ManagementObjectSearcher("root\\CIMV2",
                "SELECT Name, Domain FROM Win32_UserAccount WHERE LocalAccount = TRUE AND Disabled = FALSE");
            using var users = searcher.Get();
            foreach (ManagementObject user in users)
            {
                using (user)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var name = user["Name"]?.ToString();
                    var domain = user["Domain"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(domain))
                        names.Add($"{domain}\\{name}");
                }
            }
            return names.OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }, cancellationToken);
    }
}
