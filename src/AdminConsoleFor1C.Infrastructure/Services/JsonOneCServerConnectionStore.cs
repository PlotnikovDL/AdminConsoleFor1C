using System.Text.Json;
using AdminConsoleFor1C.Application.Services;
using AdminConsoleFor1C.Core.Administration;

namespace AdminConsoleFor1C.Infrastructure.Services;

public sealed class JsonOneCServerConnectionStore(string path) : IOneCServerConnectionStore
{
    public async Task<IReadOnlyList<OneCServerConnectionProfile>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return [];
        var profiles = JsonSerializer.Deserialize<OneCServerConnectionProfile[]>(
            await File.ReadAllTextAsync(path, cancellationToken))
            ?? throw new InvalidDataException("Не удалось прочитать сохранённые подключения.");
        foreach (var profile in profiles) profile.Validate();
        if (profiles.Select(p => p.Id).Distinct().Count() != profiles.Length)
            throw new InvalidDataException("В файле подключений повторяются идентификаторы.");
        return profiles;
    }

    public async Task SaveAsync(IReadOnlyList<OneCServerConnectionProfile> profiles, CancellationToken cancellationToken = default)
    {
        foreach (var profile in profiles) profile.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(profiles,
                new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
