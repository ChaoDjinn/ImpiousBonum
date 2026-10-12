namespace ImpiousBonum.App.Shell;

/// <summary>How the dashboard window behaves, as shown and changed on the Screens page.</summary>
public sealed record WindowOptions(bool Windowed, bool Locked, bool ClickThrough, bool AlwaysOnTop, bool HideOverFullscreen);
