namespace ImpiousBonum.App.Shell;

/// <summary>
/// Where the app's help lives on GitHub. Installed copies link to the docs of their own release, so the guide matches
/// what they do; development builds link to main.
/// </summary>
public static class Links
{
    public const string Repository = "https://github.com/ChaoDjinn/ImpiousBonum";

    /// <summary>A new issue, with the choice of a bug report or an idea.</summary>
    public const string NewIssue = Repository + "/issues/new/choose";

    public const string License = Repository + "/blob/main/LICENSE";

    /// <param name="version">The installed version, e.g. 0.10.0, or null for a development build.</param>
    public static string Guide(string? version) => Doc(version, "docs/guide.md");

    public static string Widgets(string? version) => Doc(version, "docs/widgets.md");

    public static string Changelog(string? version) => Doc(version, "CHANGELOG.md");

    private static string Doc(string? version, string path) => $"{Repository}/blob/{(version is null ? "main" : $"v{version}")}/{path}";
}
