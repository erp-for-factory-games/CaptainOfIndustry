namespace ErpForFactoryGames.CaptainOfIndustry.Catalogue;

/// <summary>
/// Finds a local Captain of Industry installation, and the <c>Managed</c>
/// directory holding the assemblies the catalogue is read from.
/// </summary>
public static class CoiInstallLocator
{
    public const string InstallPathEnvironmentVariable = "ERP_CAPTAIN_OF_INDUSTRY_INSTALL_PATH";

    private const string DataDirectoryName = "Captain of Industry_Data";
    private const string SteamRelativePath = @"steamapps\common\Captain of Industry";

    /// <summary>The assemblies that must be present for a directory to be usable.</summary>
    public static readonly IReadOnlyList<string> RequiredAssemblies =
        ["Mafi.dll", "Mafi.Core.dll", "Mafi.Base.dll"];

    /// <summary>
    /// Resolution order: an explicit path, then the environment variable, then
    /// the default Steam locations.
    /// </summary>
    public static string? ResolveInstallDirectory(string? configuredPath = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return IsInstallDirectory(configuredPath) ? configuredPath : null;
        }

        var fromEnvironment = Environment.GetEnvironmentVariable(InstallPathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment) && IsInstallDirectory(fromEnvironment))
        {
            return fromEnvironment;
        }

        return DefaultInstallDirectories().FirstOrDefault(IsInstallDirectory);
    }

    /// <summary>True if the directory looks like a game install with the assemblies present.</summary>
    public static bool IsInstallDirectory(string? path)
    {
        var managed = ManagedDirectory(path);
        return managed is not null
               && RequiredAssemblies.All(dll => File.Exists(Path.Combine(managed, dll)));
    }

    /// <summary>
    /// The <c>Managed</c> directory for an install root, or null if the layout
    /// doesn't match. Also accepts being handed the <c>Managed</c> path itself,
    /// since that's an easy mistake to make from the command line.
    /// </summary>
    public static string? ManagedDirectory(string? installDirectory)
    {
        if (string.IsNullOrWhiteSpace(installDirectory)) return null;

        var direct = Path.Combine(installDirectory, DataDirectoryName, "Managed");
        if (Directory.Exists(direct)) return direct;

        return Directory.Exists(installDirectory)
               && Path.GetFileName(installDirectory.TrimEnd(Path.DirectorySeparatorChar))
                   .Equals("Managed", StringComparison.OrdinalIgnoreCase)
            ? installDirectory
            : null;
    }

    /// <summary>Steam's default install locations across the usual drive letters.</summary>
    public static IEnumerable<string> DefaultInstallDirectories()
    {
        if (!OperatingSystem.IsWindows()) yield break;

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrEmpty(programFilesX86))
        {
            yield return Path.Combine(programFilesX86, "Steam", SteamRelativePath);
        }

        // Secondary Steam libraries are conventionally SteamLibrary at a drive root.
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
        {
            yield return Path.Combine(drive.RootDirectory.FullName, "SteamLibrary", SteamRelativePath);
        }
    }
}
