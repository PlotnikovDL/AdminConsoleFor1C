using AdminConsoleFor1C.Core.Administration;

namespace AdminConsoleFor1C.Application.Services;

// Display legacy automatic names consistently without rewriting saved profiles or user aliases.
public static class ServerConnectionPresentation
{
    private static readonly IComparer<string> NaturalText = Comparer<string>.Create(CompareNatural);

    public static bool IsLocalHost(string host, string machineName)
        => host.Equals(machineName, StringComparison.OrdinalIgnoreCase)
            || host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host is "127.0.0.1" or "::1";

    public static string SuggestedName(string host, int port, string machineName)
    {
        host = host.Trim();
        if (host.Length == 0) return string.Empty;
        return Address(IsLocalHost(host, machineName) ? machineName : host, port);
    }

    public static string DisplayName(OneCServerConnectionProfile profile, string machineName)
    {
        var name = profile.Name;
        var automatic = name.Equals(profile.AgentAddress, StringComparison.OrdinalIgnoreCase)
            || name.Equals($"{profile.Host}:{profile.AgentPort}", StringComparison.OrdinalIgnoreCase);
        if (IsLocalHost(profile.Host, machineName))
        {
            automatic |= new[] { machineName, "localhost", "127.0.0.1", "::1" }
                .Any(host => name.Equals(Address(host, profile.AgentPort), StringComparison.OrdinalIgnoreCase));
            automatic |= name.Equals($"Этот компьютер · {profile.PlatformVersion}", StringComparison.OrdinalIgnoreCase);
        }
        return automatic ? SuggestedName(profile.Host, profile.AgentPort, machineName) : name;
    }

    public static IEnumerable<T> OrderForDisplay<T>(IEnumerable<T> connections,
        Func<T, OneCServerConnectionProfile> profileSelector, Func<T, bool> localSourceSelector, string machineName)
    {
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(profileSelector);
        ArgumentNullException.ThrowIfNull(localSourceSelector);
        ArgumentNullException.ThrowIfNull(machineName);

        return connections.Select(item =>
        {
            var profile = profileSelector(item);
            var localHost = IsLocalHost(profile.Host, machineName);
            return new
            {
                Item = item,
                Profile = profile,
                IsLocal = localSourceSelector(item) || localHost,
                Host = localHost ? machineName : profile.Host,
                Name = DisplayName(profile, machineName)
            };
        })
            .OrderBy(item => item.IsLocal ? 0 : 1)
            .ThenBy(item => item.IsLocal ? item.Host : item.Name, NaturalText)
            .ThenBy(item => item.Host, NaturalText)
            .ThenBy(item => item.Profile.AgentPort)
            .ThenBy(item => item.Profile.PlatformVersion, NaturalText)
            .ThenBy(item => item.Name, NaturalText)
            .ThenBy(item => item.Profile.Id)
            .Select(item => item.Item);
    }

    private static int CompareNatural(string? left, string? right)
    {
        if (ReferenceEquals(left, right)) return 0;
        if (left is null) return -1;
        if (right is null) return 1;

        var leftIndex = 0;
        var rightIndex = 0;
        var leadingZeroComparison = 0;
        while (leftIndex < left.Length && rightIndex < right.Length)
        {
            if (char.IsAsciiDigit(left[leftIndex]) && char.IsAsciiDigit(right[rightIndex]))
            {
                var leftEnd = leftIndex;
                var rightEnd = rightIndex;
                while (leftEnd < left.Length && char.IsAsciiDigit(left[leftEnd])) leftEnd++;
                while (rightEnd < right.Length && char.IsAsciiDigit(right[rightEnd])) rightEnd++;
                var leftNumber = leftIndex;
                var rightNumber = rightIndex;
                while (leftNumber < leftEnd && left[leftNumber] == '0') leftNumber++;
                while (rightNumber < rightEnd && right[rightNumber] == '0') rightNumber++;

                var comparison = (leftEnd - leftNumber).CompareTo(rightEnd - rightNumber);
                if (comparison == 0)
                    comparison = left.AsSpan(leftNumber, leftEnd - leftNumber)
                        .SequenceCompareTo(right.AsSpan(rightNumber, rightEnd - rightNumber));
                if (comparison != 0) return comparison;
                if (leadingZeroComparison == 0)
                    leadingZeroComparison = (leftEnd - leftIndex).CompareTo(rightEnd - rightIndex);
                leftIndex = leftEnd;
                rightIndex = rightEnd;
                continue;
            }

            var textComparison = left.AsSpan(leftIndex, 1)
                .CompareTo(right.AsSpan(rightIndex, 1), StringComparison.OrdinalIgnoreCase);
            if (textComparison != 0) return textComparison;
            leftIndex++;
            rightIndex++;
        }

        var remaining = (left.Length - leftIndex).CompareTo(right.Length - rightIndex);
        return remaining != 0 ? remaining : leadingZeroComparison;
    }

    private static string Address(string host, int port) => host.Contains(':') ? $"[{host}]:{port}" : $"{host}:{port}";
}
