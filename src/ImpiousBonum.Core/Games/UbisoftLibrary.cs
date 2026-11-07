namespace ImpiousBonum.Core.Games;

/// <summary>
/// The games Ubisoft Connect has installed, from its registry entries
/// (<c>HKEY_LOCAL_MACHINE\SOFTWARE\Ubisoft\Launcher\Installs\&lt;id&gt;</c>, with each game's folder) and the uninstall
/// entry Windows keeps for each (<c>Uplay Install &lt;id&gt;</c>, with its name and exe). A game is started through
/// Ubisoft Connect (<c>uplay://launch/&lt;id&gt;/0</c>), as its shortcuts do. Nothing is fetched.
/// </summary>
public sealed class UbisoftLibrary(IGameFiles files)
{
    /// <summary>Where Ubisoft Connect lists its games, in the registry's 32-bit view.</summary>
    public const string RegistryPath = @"SOFTWARE\Ubisoft\Launcher\Installs";

    /// <summary>Every game Ubisoft Connect has installed; none without it.</summary>
    public IReadOnlyList<StoreGame> Games()
    {
        var installs = files.LocalMachineEntries(RegistryPath);
        if (installs.Count == 0)
            return [];
        var uninstall = files.LocalMachineEntries(BattleNetLibrary.RegistryPath)
            .Where(e => e.Key.StartsWith("Uplay Install ", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(e => e.Key["Uplay Install ".Length..].Trim(), StringComparer.OrdinalIgnoreCase);
        return [.. installs.Select(install => Entry(install, uninstall.GetValueOrDefault(install.Key))).OfType<StoreGame>()];
    }

    /// <summary>The game an install entry describes, named by its uninstall entry where there is one; null without a folder.</summary>
    internal static StoreGame? Entry(RegistryEntry install, RegistryEntry? uninstall)
    {
        // Ubisoft writes the folder with forward slashes and one at the end.
        if (InstallFolders.GameFolder(install.Text("InstallDir")) is not { } folder)
            return null;
        var exe = BattleNetLibrary.ExeOf(uninstall?.Text("DisplayIcon"));
        var name = (uninstall?.Text("DisplayName") ?? Path.GetFileName(folder)).Trim();
        return new StoreGame(GameStore.Ubisoft, install.Key, name, folder, $"uplay://launch/{Uri.EscapeDataString(install.Key)}/0") { Exe = exe };
    }
}
