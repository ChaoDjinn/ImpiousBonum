using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using ImpiousBonum.Core.Remote;
using QRCoder;

namespace ImpiousBonum.App.Shell;

/// <summary>
/// Tablet and USB screen → Show QR code: the link as a QR code and as text, with copy and new-link buttons. The link
/// shows the main dashboard, or a saved layout of its own.
/// </summary>
public partial class TabletLinkWindow : Window
{
    private const string Intro = "Scan the code, or open the link, on a tablet or phone on the same Wi-Fi.";

    private readonly int _port;
    private readonly Func<string> _newToken;
    private string _token;
    private string? _layout;

    /// <param name="newToken">Replaces the link's secret (stopping old links) and returns the new one.</param>
    /// <param name="layout">The saved layout the link shows, or null for the main dashboard.</param>
    public TabletLinkWindow(int port, string token, Func<string> newToken, string? layout = null)
    {
        InitializeComponent();
        _port = port;
        _token = token;
        _newToken = newToken;
        _layout = layout;
        IntroText.Text = IntroFor(layout);

        var addresses = TabletAccess.LocalAddresses();
        foreach (var address in addresses)
            AddressBox.Items.Add(address);
        AddressBox.Visibility = addresses.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        if (addresses.Count > 0)
        {
            AddressBox.SelectedIndex = 0;
        }
        else
        {
            LinkBox.Visibility = Visibility.Collapsed;
            NoNetworkText.Visibility = Visibility.Visible;
            CopyButton.IsEnabled = false;
        }
    }

    /// <summary>The link on the PC's first local address, or null when it has none.</summary>
    /// <param name="layout">The saved layout the link shows, or null for the main dashboard.</param>
    public static string? FirstLink(int port, string token, string? layout = null) =>
        TabletAccess.LocalAddresses() is [var address, ..] ? TabletAccess.Link(address, port, token, layout) : null;

    /// <summary>Shows the link for another layout (null for the main dashboard's), when the window's already open.</summary>
    public void ShowLayout(string? layout)
    {
        _layout = layout;
        IntroText.Text = IntroFor(layout);
        ShowLink();
    }

    private static string IntroFor(string? layout) => layout is null ? Intro : $"{Intro} It shows the layout \"{layout}\", whatever the dashboard shows.";

    private string? CurrentLink => AddressBox.SelectedItem is IPAddress address ? TabletAccess.Link(address, _port, _token, _layout) : null;

    private void ShowLink()
    {
        if (CurrentLink is not { } link)
            return;
        LinkBox.Text = link;
        QrImage.Source = QrCode(link);
    }

    private static BitmapImage QrCode(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(8);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = new MemoryStream(png);
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void OnAddressChanged(object sender, SelectionChangedEventArgs e) => ShowLink();

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (CurrentLink is { } link)
            CopyToClipboard(link);
    }

    /// <summary>Another app can have the clipboard open for a moment; not worth an error.</summary>
    public static bool CopyToClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }

    private void OnNewLink(object sender, RoutedEventArgs e)
    {
        _token = _newToken();
        ShowLink();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
