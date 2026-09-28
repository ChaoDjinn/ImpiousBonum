using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using ImpiousBonum.Core.Remote;
using QRCoder;

namespace ImpiousBonum.App.Shell;

/// <summary>Tray → Display → Tablet view → Show link: the link as a QR code and as text, with copy and new-link buttons.</summary>
public partial class TabletLinkWindow : Window
{
    private readonly int _port;
    private readonly Func<string> _newToken;
    private string _token;

    /// <param name="newToken">Replaces the link's secret (stopping old links) and returns the new one.</param>
    public TabletLinkWindow(int port, string token, Func<string> newToken)
    {
        InitializeComponent();
        _port = port;
        _token = token;
        _newToken = newToken;

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
    public static string? FirstLink(int port, string token) =>
        TabletAccess.LocalAddresses() is [var address, ..] ? TabletAccess.Link(address, port, token) : null;

    private string? CurrentLink => AddressBox.SelectedItem is IPAddress address ? TabletAccess.Link(address, _port, _token) : null;

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
