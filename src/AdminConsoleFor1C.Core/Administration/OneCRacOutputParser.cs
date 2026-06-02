namespace AdminConsoleFor1C.Core.Administration;

public static class OneCRacOutputParser
{
    public static IReadOnlyList<IReadOnlyDictionary<string, string>> ParseObjects(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        var objects = new List<IReadOnlyDictionary<string, string>>();
        var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in output.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                AddCurrentObject(objects, current);
                current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                continue;
            }

            var separatorIndex = line.IndexOf(':', StringComparison.Ordinal);
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            if (key.Length > 0)
            {
                current[key] = value;
            }
        }

        AddCurrentObject(objects, current);
        return objects;
    }

    private static void AddCurrentObject(
        List<IReadOnlyDictionary<string, string>> objects,
        Dictionary<string, string> current)
    {
        if (current.Count > 0)
        {
            objects.Add(new Dictionary<string, string>(current, StringComparer.OrdinalIgnoreCase));
        }
    }
}
