using System.Text.Json;
using Horizon.Core.Interfaces;
using Horizon.Core.Models;

namespace Horizon.Infrastructure.Persistence;

public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => LoadAsync(LocalPaths.Settings, new AppSettings(), cancellationToken);
    public Task<UserProfile> LoadProfileAsync(CancellationToken cancellationToken = default) => LoadAsync(LocalPaths.Profile, new UserProfile(), cancellationToken);
    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) => SaveAsync(LocalPaths.Settings, settings, cancellationToken);
    public Task SaveProfileAsync(UserProfile profile, CancellationToken cancellationToken = default) => SaveAsync(LocalPaths.Profile, profile, cancellationToken);

    public Task ResetAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(LocalPaths.Settings)) File.Delete(LocalPaths.Settings);
        if (File.Exists(LocalPaths.Profile)) File.Delete(LocalPaths.Profile);
        return Task.CompletedTask;
    }

    private static async Task<T> LoadAsync<T>(string path, T fallback, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return fallback;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, Options, cancellationToken) ?? fallback;
    }

    private static async Task SaveAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var temp = path + ".tmp";
        await using (var stream = File.Create(temp)) await JsonSerializer.SerializeAsync(stream, value, Options, cancellationToken);
        File.Move(temp, path, true);
    }
}
