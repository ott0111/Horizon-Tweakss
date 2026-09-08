using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Horizon.Core.Interfaces;
using Horizon.Core.Models;

namespace Horizon.Infrastructure.Persistence;

public sealed class DpapiSessionStore : ISecureSessionStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Horizon.Windows.Authentication.v1");
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public async Task<StoredAuthenticationSession?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(LocalPaths.AuthenticationSession)) return null;
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            var protectedBytes = await File.ReadAllBytesAsync(LocalPaths.AuthenticationSession, cancellationToken);
            var bytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<StoredAuthenticationSession>(bytes, Options);
        }
        catch (CryptographicException)
        {
            await ClearAsync(cancellationToken);
            return null;
        }
        catch (JsonException)
        {
            await ClearAsync(cancellationToken);
            return null;
        }
    }

    public async Task SaveAsync(StoredAuthenticationSession session, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Secure Horizon sessions require Windows DPAPI.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(session, Options);
        var protectedBytes = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
        var temporary = LocalPaths.AuthenticationSession + ".tmp";
        await File.WriteAllBytesAsync(temporary, protectedBytes, cancellationToken);
        File.Move(temporary, LocalPaths.AuthenticationSession, true);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(LocalPaths.AuthenticationSession)) File.Delete(LocalPaths.AuthenticationSession);
        return Task.CompletedTask;
    }
}
