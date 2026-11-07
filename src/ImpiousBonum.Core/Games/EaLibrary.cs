namespace ImpiousBonum.Core.Games;

/// <summary>
/// The games the EA app (or Origin before it) has installed, from the uninstall entry Windows keeps for each: EA's
/// installer writes one whose uninstaller is its own (<c>…\EAInstaller\…\Cleanup.exe</c>), and leaves its notes in the
/// game's <c>__Installer</c> folder. Each game's name, folder and exe come from the entry. A game is started by its exe,
/// which brings up the EA app to sign in as EA's games do. Nothing is fetched.
/// </summary>
public sealed class EaLibrary(IGameFiles files)
{
    /// <summary>Every game EA has installed whose exe is still there; none without the EA app.</summary>
    public IReadOnlyList<StoreGame> Games() =>
        [.. files.LocalMachineEntries(BattleNetLibrary.RegistryPath)
            .Where(IsEas)
            .Select(Entry)
            .OfType<StoreGame>()
            .Where(game => game.Exe is not null && files.FileExists(game.Exe))
            .DistinctBy(game => game.Folder, StringComparer.OrdinalIgnoreCase)];

    /// <summary>Whether EA's installer wrote the entry: its uninstaller, or its notes in the game's folder.</summary>
    private bool IsEas(RegistryEntry entry) =>
        entry.Text("UninstallString")?.Contains(@"\EAInstaller\", StringComparison.OrdinalIgnoreCase) == true
        || InstallFolders.GameFolder(entry.Text("InstallLocation")) is { } folder && files.FileExists(Path.Combine(folder, "__Installer", "installerdata.xml"));

    /// <summary>The game an entry describes, or null if it has no folder.</summary>
    internal static StoreGame? Entry(RegistryEntry entry)
    {
        if (InstallFolders.GameFolder(entry.Text("InstallLocation")) is not { } folder)
            return null;
        var exe = BattleNetLibrary.ExeOf(entry.Text("DisplayIcon"));
        return new StoreGame(GameStore.Ea, entry.Key, Tidy(entry.Text("DisplayName") ?? Path.GetFileName(folder)), folder, exe)
        {
            Exe = exe,
            WorkingDirectory = exe is null ? null : Path.GetDirectoryName(exe),
        };
    }

    /// <summary>"STAR WARS Jedi - Fallen Order™" → "STAR WARS Jedi - Fallen Order": the marks don't help a tile.</summary>
    internal static string Tidy(string name) => name.Replace("™", "").Replace("®", "").Replace("©", "").Trim();
}
