namespace Horizon.Infrastructure.Persistence;

public static class LocalPaths
{
    public static string Root
    {
        get
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Horizon");
            Directory.CreateDirectory(path);
            return path;
        }
    }

    public static string Settings => Path.Combine(Root, "settings.json");
    public static string Profile => Path.Combine(Root, "profile.json");
    public static string AuthenticationSession => Path.Combine(Root, "auth.session");
    public static string Database => Path.Combine(Root, "horizon.db");
    public static string ActiveChanges => Path.Combine(Root, "active-changes.json");
    public static string Entitlements => Path.Combine(Root, "entitlements.json");
    public static string GamingMode => Path.Combine(Root, "gaming-mode.json");
    public static string Logs => Path.Combine(Root, "Logs");
}
