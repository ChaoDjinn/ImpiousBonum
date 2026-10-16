using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using Microsoft.Win32;

namespace ImpiousBonum.App.Shell;

/// <summary>What's on the Windows desktop: a picture (its file), or a solid colour when <see cref="Picture"/> is null.</summary>
public sealed record Wallpaper(string? Picture, Color Colour);

/// <summary>The Windows desktop's wallpaper, for making a theme to match it.</summary>
public static class WindowsWallpaper
{
    private const uint GetDeskWallpaper = 0x0073;
    private const int DesktopColour = 1;

    /// <summary>The wallpaper showing now. With a slideshow or Windows Spotlight, the picture showing at the moment.</summary>
    public static Wallpaper Current()
    {
        var buffer = new char[2048];
        var picture = SystemParametersInfoW(GetDeskWallpaper, (uint)buffer.Length, buffer, 0) ? new string(buffer).TrimEnd('\0') : string.Empty;
        var colour = GetSysColor(DesktopColour);
        var copy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes\TranscodedWallpaper");
        return Choose(picture, copy, BackgroundType(), Color.FromRgb((byte)colour, (byte)(colour >> 8), (byte)(colour >> 16)));
    }

    /// <summary>Which of what Windows says is the wallpaper to use.</summary>
    /// <param name="picture">The wallpaper's file as Windows gives it, or empty when there's no picture.</param>
    /// <param name="copy">The copy Windows keeps of the picture showing, which a slideshow or Spotlight changes.</param>
    /// <param name="type">Settings → Personalisation → Background: 0 a picture, 1 a solid colour, 2 a slideshow, 3 Spotlight; null if unknown.</param>
    /// <param name="colour">The desktop's colour, shown when there's no picture.</param>
    internal static Wallpaper Choose(string picture, string copy, int? type, Color colour)
    {
        if (type == 1)
            return new Wallpaper(null, colour);
        if (picture.Length > 0 && File.Exists(picture))
            return new Wallpaper(picture, colour);
        // A slideshow or Spotlight, or a picture since moved: Windows' copy is the one showing.
        if ((picture.Length > 0 || type is not null) && File.Exists(copy))
            return new Wallpaper(copy, colour);
        return new Wallpaper(null, colour);
    }

    private static int? BackgroundType()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Wallpapers");
            return key?.GetValue("BackgroundType") as int?;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoW(uint action, uint param, char[] buffer, uint winIni);

    [DllImport("user32.dll")]
    private static extern uint GetSysColor(int index);
}
