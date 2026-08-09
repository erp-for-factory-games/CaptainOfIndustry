using ErpForFactoryGames.CaptainOfIndustry.Application;

namespace ErpForFactoryGames.CaptainOfIndustry.Infrastructure;

/// <summary>
/// Finds a Captain of Industry installation: an explicit path, then the
/// environment variable, then the usual Steam locations.
/// </summary>
public sealed class SteamCoiInstallLocator : ICoiInstallLocator
{
    public const string InstallPathEnvironmentVariable = "ERP_CAPTAIN_OF_INDUSTRY_INSTALL_PATH";

    private const string DataDirectoryName = "Captain of Industry_Data";
    private const string SteamRelativePath = @"steamapps\common\Captain of Industry";

    /// <summary>Assemblies that must be present for a directory to be usable.</summary>
    public static readonly IReadOnlyList<string> RequiredAssemblies =
        ["Mafi.dll", "Mafi.Core.dll", "Mafi.Base.dll"];

    public string? Resolve(string? configuredPath = null)
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

    public bool IsInstallDirectory(string? path)
    {
        var managed = ManagedDirectory(path);
        return managed is not null
               && RequiredAssemblies.All(dll => File.Exists(Path.Combine(managed, dll)));
    }

    /// <summary>
    /// The <c>Managed</c> directory for an install root, or null when the layout
    /// doesn't match. Also accepts the <c>Managed</c> path itself — an easy thing
    /// to pass by mistake from a command line.
    /// </summary>
    public static string? ManagedDirectory(string? installDirectory)
    {
        if (string.IsNullOrWhiteSpace(installDirectory)) return null;

        var nested = Path.Combine(installDirectory, DataDirectoryName, "Managed");
        if (Directory.Exists(nested)) return nested;

        return Directory.Exists(installDirectory)
               && Path.GetFileName(installDirectory.TrimEnd(Path.DirectorySeparatorChar))
                   .Equals("Managed", StringComparison.OrdinalIgnoreCase)
            ? installDirectory
            : null;
    }

    /// <summary>Steam's default locations, primary library first.</summary>
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
