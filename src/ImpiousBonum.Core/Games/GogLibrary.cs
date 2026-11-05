namespace ImpiousBonum.Core.Games;

/// <summary>
/// The games GOG Galaxy, or a GOG installer, has installed, from the registry entry each gets under
/// <c>HKEY_LOCAL_MACHINE\SOFTWARE\GOG.com\Games</c>: the game's name, the folder it's in, and its exe and the folder to
/// start it in, which start it directly since GOG's games don't need a launcher running. Nothing is fetched. Add-ons, which name the game they
/// depend on, are left out.
/// </summary>
public sealed class GogLibrary(IGameFiles files)
{
    /// <summary>Where GOG lists its games, in the registry's 32-bit view.</summary>
    public const string RegistryPath = @"SOFTWARE\GOG.com\Games";

    /// <summary>Every game GOG has installed; none without it.</summary>
    public IReadOnlyList<StoreGame> Games() => [.. files.LocalMachineEntries(RegistryPath).Select(Entry).OfType<StoreGame>()];

    /// <summary>The game a registry entry describes, or null if it has no folder or is an add-on.</summary>
    internal static StoreGame? Entry(RegistryEntry entry)
    {
        if (InstallFolders.GameFolder(entry.Text("path")) is not { } folder || entry.Text("dependsOn") is not null)
            return null;
        var id = entry.Text("gameID") ?? entry.Key;
        var name = (entry.Text("gameName") ?? Path.GetFileName(folder)).Trim();
        var exe = entry.Text("exe");
        return new StoreGame(GameStore.Gog, id, name, folder, exe, entry.Text("launchParam") ?? "")
        {
            Exe = exe,
            // GOG's own shortcuts start the game here; DOSBox and ScummVM releases find their settings by it.
            WorkingDirectory = entry.Text("workingDir") ?? (exe is null ? null : Path.GetDirectoryName(exe)),
        };
    }
}
